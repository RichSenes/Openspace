import { useEffect, useRef, type ReactNode } from 'react'
import { Maximize } from 'lucide-react'
import { Application, Container, Graphics, Sprite, Text, Texture } from 'pixi.js'
import type { DeskActivity, DeskContext, FloorCut, Intern, OfficeView, Sky, WorkTaskView } from '../api/types'
import { paintInto } from '../art/canvas'
import { inTheDark, unlit, type Light } from '../art/lighting'
import { deskItem, internItems, propItem, roomPixels, roomShell, upperWall, WALL_HEIGHT } from '../art/roomScene'
import { sceneLights, scenePixels, type SceneItem } from '../art/scene'
import { HALL_FLOOR } from '../art/sheets/rooms'
import { pixelsOf, type Pixel } from '../art/sprite'
import { STATION, themeColors } from '../art/station'
import { themeOf, type Theme } from '../art/themes'
import { doorwayPixels, GLASS, phaseOf, skyPixels, windowLight, windowPixels, WINDOW_SIZE, type DayPhase } from '../art/windows'
import type { AgentState } from '../art/agentStates'
import { energyOf } from '../activity/energy'
import { InternStage } from '../activity/interns'
import { WalkStage, type DeskDoing } from '../activity/walks'
import { walkerItems } from '../art/chat'
import { WALKER } from '../art/walker'
import { placesOf } from './stations'
import { agentStateOf, lookOf } from './agents'
import { bubbleText, placeBubbles, type PlacedBubble } from './bubbles'
import {
  arrangementOf,
  boundsOf,
  BUILDING,
  commonAt,
  commonBounds,
  floorArrangementOf,
  handlesOf,
  place,
  placeCommon,
  stretchCut,
  targetAt,
  WINDOW,
  type Edge,
  type Target,
} from './arrange'
import { between, fit, pan, toWorld, wheelFactor, zoomAt, type Camera } from './camera'
import { doorOf, inBuilding, layoutOffice, TILE, WALL, type BuildingLayout, type RoomBox } from './layout'
import { wallPlan } from './walls'
import { centredOn, inkOf, mapSize, mapView, minimapPixels, type MapAgent, type RoomInk } from './minimap'

/** Frames per second of the pixel art, as in docs/art-style.md. */
const FPS = 5

const COLORS = {
  ground: 0x14161c,
  roomEdge: 0x0b0c10,
  label: 0xece8df,
  mute: 0xa3a8b6,
  bubble: 0xece8df,
  bubbleText: 0x14161c,
  waiting: 0xf2b84b,
  selected: 0xf2b84b,
}

const BUBBLE_WIDTH = 132

interface Props {
  office: OfficeView
  /** The office's tasks: they tell what each agent is up to. */
  tasks: readonly WorkTaskView[]
  /** What each desk is doing, by desk id: shown as speech bubbles. */
  activities: ReadonlyMap<string, DeskActivity>
  /** States decided elsewhere, by desk id: the routines' dolls. */
  states?: ReadonlyMap<string, AgentState>
  /** How full each desk's conversation is, by desk id: the battery beside each agent. */
  contexts?: ReadonlyMap<string, DeskContext>
  /** The helpers working for each desk, by desk id: interns standing beside it. */
  interns?: ReadonlyMap<string, readonly Intern[]>
  /** Lights off: only screens, lamps and other glowing things light the rooms. */
  dark: boolean
  /** The weather of the person's town, at the windows; clear when none is set. */
  weather?: Sky
  /** A little map of the building in a corner: where the screen is, and every agent. */
  minimap?: boolean
  /** More controls beside Fit. */
  controls?: ReactNode
  /** Present while the person arranges rooms: things can then be picked up and moved. */
  arrange?: ArrangeControls
  /** A click on an agent or its bubble, with where it happened on the canvas; null for a click elsewhere. */
  onPickDesk?: (pick: DeskPick | null) => void
}

export interface DeskPick {
  deskId: string
  /** Canvas pixels: where to show what belongs to the desk. */
  x: number
  y: number
}

/** A room being arranged, or the building itself (BUILDING), and possibly the thing picked in it. */
export interface Selection {
  roomId: string
  target?: Target
}

export interface ArrangeControls {
  selection: Selection | null
  onSelect: (selection: Selection | null) => void
  /** A target was dropped at (x, y), in pixels from its room's corner, or the building's. */
  onPlace: (roomId: string, target: Target, x: number, y: number) => void
  /** A room's edge was dragged: its new size in tiles, wall to wall and floor depth. */
  onResize: (roomId: string, width: number, depth: number) => void
  /** A room was dragged by its back wall to a new place, in tiles from the building's corner. */
  onMoveRoom: (roomId: string, left: number, top: number) => void
  /** The building's edge was dragged: its new size in tiles. */
  onResizeFloor: (width: number, depth: number) => void
  /** A cut of the building was dragged or stretched: where it goes, in tiles from the building's corner. */
  onCut: (index: number, cut: FloorCut) => void
}

const EDGE_CURSORS: Record<Edge, string> = { left: 'ew-resize', top: 'ns-resize', right: 'ew-resize', bottom: 'ns-resize', corner: 'nwse-resize' }

/** Draws the selected office as a pixel-art building you can zoom, pan and fly through. */
export function BuildingCanvas({ office, tasks, activities, states, contexts, interns, dark, weather, minimap, controls, arrange, onPickDesk }: Props) {
  const host = useRef<HTMLDivElement>(null)
  const appRef = useRef<Application | null>(null)
  const draw = useRef<() => void>(() => {})
  const fitAll = useRef<() => void>(() => {})
  const arrangeRef = useRef(arrange)
  const pickRef = useRef(onPickDesk)
  // Remembers who stands beside each desk across redraws, so arrivals and departures can puff.
  const stage = useRef(new InternStage())
  // Remembers who is away from their desk, so walks carry on across redraws.
  const walks = useRef(new WalkStage())
  const mapCanvas = useRef<HTMLCanvasElement>(null)
  const mapFrame = useRef<HTMLDivElement>(null)

  // Refreshed after every render so the canvas callbacks always see the latest props.
  useEffect(() => {
    arrangeRef.current = arrange
    pickRef.current = onPickDesk
    draw.current = () => {
      const app = appRef.current
      if (!app) return
      const view = viewOf(app)
      // The same building on every screen: only the camera follows the window.
      const layout = layoutOffice(office)
      view.layout = layout

      app.stage.removeChildren().forEach((child) => child.destroy({ children: true, texture: true, textureSource: true }))
      stage.current.update(interns ?? NO_INTERNS, Date.now(), !reducedMotion())
      // Nobody walks while rooms are arranged, nor for people who prefer less motion.
      const walking = !arrange && !reducedMotion() ? walks.current : undefined
      const drawn = drawBuilding(layout, { office, tasks, activities, states, contexts, interns: stage.current, walks: walking, dark, weather }, arrange?.selection)
      app.stage.addChild(drawn.world)
      Object.assign(view, drawn)
      view.hall?.paint()
      drawn.rooms.forEach((room) => room.paint(view.tick))
      view.map = mapPainter(layout, drawn.scene, mapCanvas.current, mapFrame.current, app)
      view.walk = walking ? walker(layout, drawn.scene, walking) : undefined
      view.walk?.()
      view.map?.paint(view.tick)
      if (view.auto) view.camera = fit(worldRect(layout), app.screen)
      apply(view)
    }
  })

  useEffect(() => {
    const element = host.current
    if (!element) return
    const app = new Application()
    let disposed = false
    const listeners: (() => void)[] = []

    void Promise.all([
      app.init({ resizeTo: element, background: COLORS.ground, antialias: false, roundPixels: true }),
      document.fonts.load('16px Silkscreen'),
    ]).then(() => {
      if (disposed) {
        app.destroy(true, { children: true })
        return
      }
      element.appendChild(app.canvas)
      appRef.current = app
      app.renderer.on('resize', () => {
        const view = viewOf(app)
        if (view.auto && view.layout) {
          view.camera = fit(worldRect(view.layout), app.screen)
          apply(view)
        }
      })
      // Pixi follows the window; the panel beside the building changes its size too.
      let size = `${element.clientWidth}x${element.clientHeight}`
      const observer = new ResizeObserver(() => {
        // Only a real change of the box, never the canvas settling inside it.
        const now = `${element.clientWidth}x${element.clientHeight}`
        if (now !== size) {
          size = now
          app.resize()
        }
      })
      observer.observe(element)
      listeners.push(() => observer.disconnect())
      fitAll.current = () => {
        const view = viewOf(app)
        view.auto = true
        if (view.layout) flyTo(view, fit(worldRect(view.layout), app.screen))
      }
      listeners.push(...attachNavigation(app, () => arrangeRef.current, (pick) => pickRef.current?.(pick)))
      draw.current()
      // The art moves at its own pace; people who prefer less motion get still rooms.
      if (!reducedMotion()) {
        const timer = setInterval(() => {
          const view = viewOf(app)
          view.tick++
          view.walk?.()
          view.rooms?.forEach((room) => room.paint(view.tick))
          view.map?.paint(view.tick)
        }, 1000 / FPS)
        listeners.push(() => clearInterval(timer))
      }
    })

    return () => {
      disposed = true
      listeners.forEach((stop) => stop())
      if (appRef.current) {
        appRef.current.destroy(true, { children: true })
        appRef.current = null
      }
    }
  }, [])

  const arranging = Boolean(arrange)
  useEffect(() => draw.current(), [office, tasks, activities, states, contexts, interns, dark, weather, minimap, arranging, arrange?.selection])

  // Pressing the map flies there; dragging across it follows the pointer.
  const goTo = (e: React.PointerEvent<HTMLCanvasElement>, glide: boolean) => {
    const app = appRef.current
    const canvas = mapCanvas.current
    if (!app || !canvas) return
    const view = viewOf(app)
    const box = canvas.getBoundingClientRect()
    const target = centredOn(view.camera, app.screen, ((e.clientX - box.left) / box.width) * canvas.width, ((e.clientY - box.top) / box.height) * canvas.height)
    view.auto = false
    if (glide) flyTo(view, target)
    else {
      if (view.animation) cancelAnimationFrame(view.animation)
      view.camera = target
      apply(view)
    }
  }

  return (
    <div className="building-canvas" ref={host}>
      <div className="building-controls">
        <span className="hint">
          {arrange
            ? 'Drag desks, furniture, doors and windows · drag a back wall to move its room · arrows nudge · Delete removes'
            : 'Scroll to zoom · drag to move · double-click a room · click an agent'}
        </span>
        <button type="button" className="btn" onClick={() => fitAll.current()}>
          <Maximize size={16} /> Fit
        </button>
        {controls}
      </div>
      {minimap && (
        <div className="minimap">
          <canvas
            ref={mapCanvas}
            aria-label="Map of the building: press to go there"
            onPointerDown={(e) => {
              e.currentTarget.setPointerCapture(e.pointerId)
              goTo(e, true)
            }}
            onPointerMove={(e) => e.currentTarget.hasPointerCapture(e.pointerId) && goTo(e, false)}
          />
          <div className="minimap-view" ref={mapFrame} />
        </div>
      )}
    </div>
  )
}

/** Camera, layout and animation of one canvas, kept beside the Pixi application. */
interface View {
  camera: Camera
  /** True until the person zooms or pans: the camera then follows the window. */
  auto: boolean
  layout?: BuildingLayout
  world?: Container
  rooms?: Map<string, RoomPainter>
  /** The furniture of the common floor, repainted while a piece of it is dragged. */
  hall?: { paint: () => void }
  /** The walls, redrawn while a door is dragged along them. */
  walls?: Graphics
  scene?: Scene
  /** Desk name labels, by desk id: they follow a desk being dragged. */
  names?: Map<string, Text>
  /** The outline of what is selected while arranging. */
  selection?: Graphics
  /** Where the speech bubbles were placed, in world pixels: they can be clicked. */
  bubbles?: PlacedBubble[]
  /** Frames of pixel art shown so far. */
  tick: number
  animation?: number
  /** The little map, when shown: repainted on every frame, its frame moved with the camera. */
  map?: MapPainter
  /** Moves the agents away from their desks on, once a frame. */
  walk?: () => void
}

const views = new WeakMap<Application, View>()

function viewOf(app: Application): View {
  let view = views.get(app)
  if (!view) {
    view = { camera: { x: 0, y: 0, scale: 1 }, auto: true, tick: 0 }
    views.set(app, view)
  }
  return view
}

function apply(view: View) {
  view.world?.position.set(Math.round(view.camera.x), Math.round(view.camera.y))
  view.world?.scale.set(view.camera.scale)
  view.map?.follow(view.camera)
}

/** What every desk is up to, for the walks: its state and its step. */
function walker(layout: BuildingLayout, scene: Scene, walks: WalkStage): () => void {
  const places = placesOf(layout)
  const doing = new Map<string, DeskDoing>(
    layout.rooms.flatMap((room) =>
      room.desks.map((d) => {
        const activity = scene.activities.get(d.id)
        return [d.id, { state: scene.states?.get(d.id) ?? agentStateOf(d.id, scene.tasks, activity), doing: activity?.doing }] as const
      }),
    ),
  )
  return () => walks.update(places, doing, Date.now())
}

interface MapPainter {
  paint: (tick: number) => void
  follow: (camera: Camera) => void
}

/**
 * The little map of the building: rooms in their mood's colours, agents as marks for their
 * state, and a frame around what the screen shows, clipped to the map.
 */
function mapPainter(layout: BuildingLayout, scene: Scene, canvas: HTMLCanvasElement | null, frame: HTMLDivElement | null, app: Application): MapPainter | undefined {
  if (!canvas || !frame) return undefined
  const size = mapSize(layout)
  canvas.style.width = `${size.width * size.zoom}px`
  canvas.style.height = `${size.height * size.zoom}px`
  const moods = new Map(scene.office.rooms.map((r) => [r.id, r.mood]))
  const office = themeOf(scene.office.theme).colors
  const hall = { ...inkOf(office), floor: office['wall.light'] }
  const inks = new Map<string, RoomInk>(layout.rooms.map((room) => [room.id, inkOf(themeOf(moods.get(room.id) ?? scene.office.theme).colors)]))
  const agents: MapAgent[] = layout.rooms.flatMap((room) =>
    room.desks.map((d) => ({ x: d.x, y: d.y, state: scene.states?.get(d.id) ?? agentStateOf(d.id, scene.tasks, scene.activities.get(d.id)) })),
  )
  // An agent away from its desk shows where it walks.
  const now = (agent: MapAgent, deskId: string): MapAgent => {
    const away = scene.walks?.at(deskId, Date.now())
    return away ? { ...agent, x: away.x + WALKER.width / 2 - STATION.width / 2, y: away.y + WALKER.height / 2 - STATION.height / 2 } : agent
  }
  const desks = layout.rooms.flatMap((room) => room.desks.map((d) => d.id))
  return {
    paint: (tick) => paintInto(canvas, size.width, size.height, minimapPixels(layout, hall, inks, agents.map((a, i) => now(a, desks[i])), tick)),
    follow: (camera) => {
      const seen = mapView(camera, app.screen)
      const left = Math.max(0, seen.x)
      const top = Math.max(0, seen.y)
      const right = Math.min(size.width, seen.x + seen.width)
      const bottom = Math.min(size.height, seen.y + seen.height)
      const whole = left <= 0 && top <= 0 && right >= size.width && bottom >= size.height
      frame.hidden = whole || right <= left || bottom <= top
      Object.assign(frame.style, {
        left: `${left * size.zoom}px`,
        top: `${top * size.zoom}px`,
        width: `${(right - left) * size.zoom}px`,
        height: `${(bottom - top) * size.zoom}px`,
      })
    },
  }
}

const worldRect = (layout: BuildingLayout) => ({ x: 0, y: 0, width: layout.width * TILE, height: layout.height * TILE })

const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches

/** Moves the camera smoothly to a target, or at once when motion is reduced. */
function flyTo(view: View, target: Camera, duration = 320) {
  if (view.animation) cancelAnimationFrame(view.animation)
  if (reducedMotion()) {
    view.camera = target
    apply(view)
    return
  }
  const from = view.camera
  const start = performance.now()
  const step = (now: number) => {
    const t = (now - start) / duration
    view.camera = between(from, target, t)
    apply(view)
    view.animation = t < 1 ? requestAnimationFrame(step) : undefined
  }
  view.animation = requestAnimationFrame(step)
}

const floorBox = (layout: BuildingLayout) => ({ x: layout.floor.x, y: layout.floor.y, width: layout.floor.width, height: layout.floor.height })

/** What a press picks up while arranging. */
type Grip =
  | { kind: 'stretch'; roomId: string; edge: Edge; cut?: number }
  | { kind: 'cut'; index: number; grab: { x: number; y: number } }
  | { kind: 'carry'; roomId: string; target: Target; from: { x: number; y: number }; grab: { x: number; y: number } }
  | { kind: 'shift'; room: RoomBox; grab: { x: number; y: number } }

/**
 * Wheel to zoom at the cursor, left-drag to pan, double-click to fly into a room. While
 * arranging, a press picks up what is under it instead. Returns the cleanups.
 */
function attachNavigation(
  app: Application,
  arrange: () => ArrangeControls | undefined,
  pickDesk: (pick: DeskPick | null) => void,
): (() => void)[] {
  const canvas = app.canvas
  const view = viewOf(app)
  const point = (e: MouseEvent) => {
    const box = canvas.getBoundingClientRect()
    return { x: e.clientX - box.left, y: e.clientY - box.top }
  }
  let drag: { x: number; y: number; id: number; moved: number } | undefined
  let grip: Grip | undefined
  // What the grip did last: the place to save on release, or the size already sent.
  let to: { x: number; y: number } | undefined
  let sent = ''

  // Rooms are redrawn as soon as something is picked: always look them up by id.
  const roomById = (id: string) => view.layout?.rooms.find((r) => r.id === id)
  const roomAt = (world: { x: number; y: number }) =>
    view.layout?.rooms.find(
      (r) => world.x >= r.x * TILE && world.x < (r.x + r.width) * TILE && world.y >= r.y * TILE && world.y < (r.y + r.height) * TILE,
    )
  const inFloor = (world: { x: number; y: number }) => !!view.layout && inBuilding(view.layout, Math.floor(world.x / TILE), Math.floor(world.y / TILE))
  /** The agent, or its bubble, under a point of the world. */
  const deskAt = (world: { x: number; y: number }) => {
    const bubble = [...(view.bubbles ?? [])].reverse().find(
      (b) => world.x >= b.left && world.x < b.left + b.width && world.y >= b.top && world.y < b.top + b.height,
    )
    if (bubble) return bubble.deskId
    return view.layout?.rooms
      .flatMap((r) => r.desks)
      .find((d) => world.x >= d.x && world.x < d.x + STATION.width && world.y >= d.y && world.y < d.y + STATION.height)?.id
  }
  const cutAt = (world: { x: number; y: number }) =>
    view.layout?.cuts.findIndex((c) => world.x >= c.x * TILE && world.x < (c.x + c.width) * TILE && world.y >= c.y * TILE && world.y < (c.y + c.height) * TILE) ?? -1

  /** The edge handle of the selected room, cut or building, a few screen pixels around the pointer. */
  const handleAt = (e: MouseEvent): Grip | undefined => {
    const selection = arrange()?.selection
    const selected = selection?.roomId
    const cut = selected === BUILDING && selection?.target?.kind === 'cut' ? selection.target.index : undefined
    const box =
      cut !== undefined ? view.layout?.cuts[cut] : selected === BUILDING ? view.layout && floorBox(view.layout) : selected ? roomById(selected) : undefined
    if (!box || !selected) return undefined
    const at = point(e)
    const handle = handlesOf(box, cut !== undefined).find((h) => {
      const x = h.x * view.camera.scale + view.camera.x
      const y = h.y * view.camera.scale + view.camera.y
      return Math.abs(at.x - x) <= 10 && Math.abs(at.y - y) <= 10
    })
    return handle && { kind: 'stretch', roomId: selected, edge: handle.edge, cut }
  }

  /** What a press at this point would pick up while arranging. */
  const gripAt = (e: MouseEvent): Grip | undefined => {
    const handle = handleAt(e)
    if (handle || !view.layout) return handle
    const world = toWorld(view.camera, point(e))
    const { floor } = view.layout
    const onFloor = { x: world.x - floor.x * TILE, y: world.y - floor.y * TILE }
    const common = commonAt(floorArrangementOf(view.layout), onFloor)
    if (common !== undefined) {
      const box = commonBounds(floorArrangementOf(view.layout), common)!
      return { kind: 'carry', roomId: BUILDING, target: { kind: 'piece', index: common }, from: { x: box.x, y: box.y }, grab: onFloor }
    }
    // Doors sit across a wall: they can be taken from either side of it.
    for (const r of view.layout.rooms) {
      const local = { x: world.x - r.x * TILE, y: world.y - r.y * TILE }
      const door = boundsOf(arrangementOf(r), { kind: 'door' })!
      if (local.x >= door.x - 3 && local.x < door.x + door.width + 3 && local.y >= door.y - 3 && local.y < door.y + door.height + 3)
        return { kind: 'carry', roomId: r.id, target: { kind: 'door' }, from: { x: door.x, y: door.y }, grab: local }
    }
    const cut = cutAt(world)
    if (cut >= 0) {
      const c = view.layout.cuts[cut]
      return { kind: 'cut', index: cut, grab: { x: Math.floor(world.x / TILE) - c.x, y: Math.floor(world.y / TILE) - c.y } }
    }
    const room = roomAt(world)
    if (!room) return undefined
    const local = { x: world.x - room.x * TILE, y: world.y - room.y * TILE }
    const target = targetAt(arrangementOf(room), local)
    if (target) {
      const box = boundsOf(arrangementOf(room), target)!
      return { kind: 'carry', roomId: room.id, target, from: { x: box.x, y: box.y }, grab: local }
    }
    // The back wall is the room's handle: drag it to move the whole room.
    if (local.y < WALL_HEIGHT) return { kind: 'shift', room, grab: { x: Math.floor(local.x / TILE), y: Math.floor(local.y / TILE) } }
    return undefined
  }

  const onWheel = (e: WheelEvent) => {
    e.preventDefault()
    if (view.animation) cancelAnimationFrame(view.animation)
    view.auto = false
    view.camera = zoomAt(view.camera, wheelFactor(e.deltaY), point(e))
    apply(view)
  }
  const onDown = (e: PointerEvent) => {
    if (e.button !== 0) return
    const controls = arrange()
    grip = controls ? gripAt(e) : undefined
    to = undefined
    sent = ''
    if (grip?.kind === 'carry') controls?.onSelect({ roomId: grip.roomId, target: grip.target })
    if (grip?.kind === 'shift') controls?.onSelect({ roomId: grip.room.id })
    if (grip?.kind === 'cut') controls?.onSelect({ roomId: BUILDING, target: { kind: 'cut', index: grip.index } })
    // Rooms and the building change shape under the pointer: the camera stays put meanwhile.
    if (grip) view.auto = false
    drag = { ...point(e), id: e.pointerId, moved: 0 }
    try {
      // Keeps the drag going when the pointer leaves the canvas.
      canvas.setPointerCapture(e.pointerId)
    } catch {
      // Some pointers cannot be captured; dragging still works inside the canvas.
    }
    canvas.style.cursor = grip?.kind === 'stretch' ? EDGE_CURSORS[grip.edge] : grip ? 'move' : 'grabbing'
  }
  const onMove = (e: PointerEvent) => {
    if (!drag) {
      if (arrange()) {
        const hover = gripAt(e)
        canvas.style.cursor = hover?.kind === 'stretch' ? EDGE_CURSORS[hover.edge] : hover ? 'move' : 'grab'
      } else {
        canvas.style.cursor = deskAt(toWorld(view.camera, point(e))) ? 'pointer' : 'grab'
      }
      return
    }
    if (drag.id !== e.pointerId) return
    const now = point(e)
    const before = drag
    drag = { ...now, id: e.pointerId, moved: before.moved + Math.abs(now.x - before.x) + Math.abs(now.y - before.y) }
    const world = toWorld(view.camera, now)
    const controls = arrange()
    const layout = view.layout
    if (grip && controls && layout) {
      if (grip.kind === 'carry') {
        const origin = grip.roomId === BUILDING ? layout.floor : roomById(grip.roomId)
        if (!origin) return
        const local = { x: world.x - origin.x * TILE, y: world.y - origin.y * TILE }
        // A door follows the pointer to the nearest wall; everything else keeps where it was held.
        const goal = grip.target.kind === 'door' ? local : { x: grip.from.x + local.x - grip.grab.x, y: grip.from.y + local.y - grip.grab.y }
        to = carryTo(view, grip.roomId, grip.target, goal.x, goal.y)
      } else if (grip.kind === 'stretch' && grip.cut !== undefined) {
        const box = layout.cuts[grip.cut]
        if (!box) return
        const cut = { left: box.x - layout.floor.x, top: box.y - layout.floor.y, width: box.width, height: box.height }
        const stretched = stretchCut(cut, grip.edge, Math.round(world.x / TILE) - layout.floor.x, Math.round(world.y / TILE) - layout.floor.y)
        const key = JSON.stringify(stretched)
        if (key !== sent && key !== JSON.stringify(cut)) {
          sent = key
          controls.onCut(grip.cut, stretched)
        }
      } else if (grip.kind === 'cut') {
        const box = layout.cuts[grip.index]
        if (!box) return
        const left = Math.floor(world.x / TILE) - grip.grab.x - layout.floor.x
        const top = Math.floor(world.y / TILE) - grip.grab.y - layout.floor.y
        const key = `${left}x${top}`
        if (key !== sent) {
          sent = key
          controls.onCut(grip.index, { left, top, width: box.width, height: box.height })
        }
      } else if (grip.kind === 'stretch') {
        const box = grip.roomId === BUILDING ? floorBox(layout) : roomById(grip.roomId)
        if (!box) return
        const width = grip.edge === 'bottom' ? box.width : Math.round(world.x / TILE - box.x)
        const height = grip.edge === 'right' ? box.height : Math.round(world.y / TILE - box.y)
        // Whole tiles only: the building is redrawn when something gains or loses one.
        const key = `${width}x${height}`
        if (key !== sent && (width !== box.width || height !== box.height)) {
          sent = key
          if (grip.roomId === BUILDING) controls.onResizeFloor(width, height)
          else controls.onResize(grip.roomId, width, height - WALL)
        }
      } else {
        const left = Math.floor(world.x / TILE) - grip.grab.x - layout.floor.x
        const top = Math.floor(world.y / TILE) - grip.grab.y - layout.floor.y
        const key = `${left}x${top}`
        if (key !== sent) {
          sent = key
          controls.onMoveRoom(grip.room.id, left, top)
        }
      }
      return
    }
    if (view.animation) cancelAnimationFrame(view.animation)
    view.auto = false
    view.camera = pan(view.camera, now.x - before.x, now.y - before.y)
    apply(view)
  }
  const onUp = (e: PointerEvent) => {
    if (drag?.id !== e.pointerId) return
    const controls = arrange()
    if (grip?.kind === 'carry' && to) controls?.onPlace(grip.roomId, grip.target, to.x, to.y)
    // A click, not a drag of the view, picks the room under it, or the building.
    else if (controls && !grip && drag.moved < 4) {
      const world = toWorld(view.camera, point(e))
      const room = roomAt(world)
      controls.onSelect(room ? { roomId: room.id } : inFloor(world) ? { roomId: BUILDING } : null)
    } else if (!controls && drag.moved < 4) {
      // A click on an agent opens what belongs to it; anywhere else closes it.
      const at = point(e)
      const desk = deskAt(toWorld(view.camera, at))
      pickDesk(desk ? { deskId: desk, ...at } : null)
    }
    drag = undefined
    grip = undefined
    canvas.style.cursor = 'grab'
  }
  const onDoubleClick = (e: MouseEvent) => {
    if (!view.layout) return
    const room = roomAt(toWorld(view.camera, point(e)))
    view.auto = !room
    flyTo(view, room ? fit({ x: room.x * TILE, y: room.y * TILE, width: room.width * TILE, height: room.height * TILE }, app.screen, 64) : fit(worldRect(view.layout), app.screen))
  }

  canvas.style.cursor = 'grab'
  canvas.addEventListener('wheel', onWheel, { passive: false })
  canvas.addEventListener('pointerdown', onDown)
  canvas.addEventListener('pointermove', onMove)
  canvas.addEventListener('pointerup', onUp)
  canvas.addEventListener('pointercancel', onUp)
  canvas.addEventListener('dblclick', onDoubleClick)
  return [
    () => canvas.removeEventListener('wheel', onWheel),
    () => canvas.removeEventListener('pointerdown', onDown),
    () => canvas.removeEventListener('pointermove', onMove),
    () => canvas.removeEventListener('pointerup', onUp),
    () => canvas.removeEventListener('pointercancel', onUp),
    () => canvas.removeEventListener('dblclick', onDoubleClick),
    () => view.animation && cancelAnimationFrame(view.animation),
  ]
}

/** A room drawn as pixel art, repainted at every frame of the animation. */
interface RoomPainter {
  paint: (tick: number) => void
}

interface Scene {
  office: OfficeView
  tasks: readonly WorkTaskView[]
  activities: ReadonlyMap<string, DeskActivity>
  contexts?: ReadonlyMap<string, DeskContext>
  interns?: InternStage
  walks?: WalkStage
  states?: ReadonlyMap<string, AgentState>
  dark: boolean
  weather?: Sky
}

const NO_INTERNS: ReadonlyMap<string, readonly Intern[]> = new Map()

/** A canvas already painted, somewhere in the world: windows look through to it. */
interface Source {
  canvas: HTMLCanvasElement
  x: number
  y: number
}

const hex = (r: number, g: number, b: number) => '#' + ((1 << 24) | (r << 16) | (g << 8) | b).toString(16).slice(1)

/** What is painted in a rectangle of the world, in pixels from its corner: the view through a window. */
function sample(sources: Source[], x: number, y: number, width: number, height: number): Pixel[] {
  const pixels: Pixel[] = []
  for (const source of sources) {
    const left = Math.max(x, source.x)
    const top = Math.max(y, source.y)
    const right = Math.min(x + width, source.x + source.canvas.width)
    const bottom = Math.min(y + height, source.y + source.canvas.height)
    if (right <= left || bottom <= top) continue
    const data = source.canvas.getContext('2d')!.getImageData(left - source.x, top - source.y, right - left, bottom - top).data
    for (let j = 0; j < bottom - top; j++)
      for (let i = 0; i < right - left; i++) {
        const k = (j * (right - left) + i) * 4
        if (data[k + 3] > 0) pixels.push({ x: left - x + i, y: top - y + j, color: hex(data[k], data[k + 1], data[k + 2]) })
      }
  }
  return pixels
}

function drawBuilding(
  layout: BuildingLayout,
  scene: Scene,
  selected: Selection | null | undefined,
): {
  world: Container
  rooms: Map<string, RoomPainter>
  hall: { paint: () => void }
  walls: Graphics
  names: Map<string, Text>
  selection: Graphics
  scene: Scene
  bubbles: PlacedBubble[]
} {
  const world = new Container()
  const labels = new Container()
  const bubbles = new Container()
  const theme = themeOf(scene.office.theme)

  const floor = hallFloor(layout, scene, theme)
  world.addChild(floor.sprite)
  const sources: Source[] = [floor.source]
  const moods = new Map(scene.office.rooms.map((r) => [r.id, r.mood]))
  const rooms = new Map(
    layout.rooms.map((room) => [room.id, drawRoom(room, layout, scene, themeOf(moods.get(room.id) ?? scene.office.theme), world, sources)]),
  )
  const walls = new Graphics()
  drawWalls(walls, layout, scene, theme)
  const hall = hallFurniture(layout, scene, theme)
  // Windows see what stands in the corridor too, over its floor.
  sources.push(hall.source)
  world.addChild(walls, hall.sprite)
  const names = new Map<string, Text>()
  const said: { text: Text; activity: DeskActivity; deskId: string; x: number; y: number }[] = []

  for (const room of layout.rooms) {
    labels.addChild(label(room.name, room.x * TILE + 6, room.y * TILE + 4, COLORS.label, 8))
    for (const desk of room.desks) {
      const name = label(desk.name, desk.x + STATION.width / 2, desk.y + STATION.height + 1, COLORS.mute, 6, true, NAME_WIDTH)
      names.set(desk.id, name)
      labels.addChild(name)
      const activity = scene.activities.get(desk.id)
      // Bubbles would hide what is being arranged.
      if (activity?.headline && selected === undefined)
        said.push({ text: bubbleWords(activity), activity, deskId: desk.id, x: desk.x + STATION.width / 2, y: desk.y - 1 })
    }
  }

  // Bubbles are measured first, then placed so that neighbours never cover each other.
  const placed = placeBubbles(
    said.map((s) => ({
      deskId: s.deskId,
      anchorX: s.x,
      anchorY: s.y,
      width: Math.min(BUBBLE_WIDTH, Math.ceil(s.text.width) + 8),
      height: Math.ceil(s.text.height) + 6,
      urgent: s.activity.kind === 'Waiting',
    })),
  )
  // Waiting bubbles are drawn last, on top of everything else.
  for (const spot of [...placed].reverse()) {
    const s = said.find((x) => x.deskId === spot.deskId)!
    bubbles.addChild(bubble(s.activity, s.text, spot))
  }

  const selection = new Graphics()
  outline(selection, layout, selected)
  // Names, then bubbles, go on top of every room, so they can spill over walls.
  world.addChild(labels, bubbles, selection)
  return { world, rooms, hall: hall.painter, walls, names, selection, scene, bubbles: placed }
}

/** Frames the room being arranged, or the building, and inside it the thing picked. */
function outline(g: Graphics, layout: BuildingLayout, selected: Selection | null | undefined) {
  g.clear()
  if (!selected) return
  const building = selected.roomId === BUILDING
  const room = building ? floorBox(layout) : layout.rooms.find((r) => r.id === selected.roomId)
  if (!room) return
  const target = selected.target
  const cut = building && target?.kind === 'cut' ? layout.cuts[target.index] : undefined
  g.rect(room.x * TILE - 3, room.y * TILE - 3, room.width * TILE + 6, room.height * TILE + 6).stroke({ width: cut ? 1 : 2, color: COLORS.selected })
  if (cut) {
    // A cut is drawn dashed, so it reads as a hole in the building and not a room.
    for (let x = cut.x * TILE; x < (cut.x + cut.width) * TILE; x += 8) {
      g.rect(x, cut.y * TILE + 1, 4, 2).fill(COLORS.selected)
      g.rect(x, (cut.y + cut.height) * TILE - 3, 4, 2).fill(COLORS.selected)
    }
    for (let y = cut.y * TILE; y < (cut.y + cut.height) * TILE; y += 8) {
      g.rect(cut.x * TILE + 1, y, 2, 4).fill(COLORS.selected)
      g.rect((cut.x + cut.width) * TILE - 3, y, 2, 4).fill(COLORS.selected)
    }
  }
  // Square handles to drag the walls out or in.
  for (const h of handlesOf(cut ?? room, Boolean(cut))) g.rect(h.x - 3, h.y - 3, 6, 6).fill(COLORS.selected).stroke({ width: 1, color: COLORS.roomEdge })
  const box = !target
    ? undefined
    : building
      ? target.kind === 'piece'
        ? commonBounds(floorArrangementOf(layout), target.index)
        : undefined
      : boundsOf(arrangementOf(layout.rooms.find((r) => r.id === selected.roomId)!), target)
  if (box) {
    const x = room.x * TILE + box.x
    const y = room.y * TILE + box.y
    g.rect(x - 1, y - 1, box.width + 2, box.height + 2).stroke({ width: 1, color: COLORS.selected })
    // Corner ticks tell the picked thing from the room around it without relying on colour.
    for (const [cx, cy, dx, dy] of [[x - 1, y - 1, 1, 1], [x + box.width + 1, y - 1, -1, 1], [x - 1, y + box.height + 1, 1, -1], [x + box.width + 1, y + box.height + 1, -1, -1]])
      g.poly([cx, cy + 4 * dy, cx, cy, cx + 4 * dx, cy], false).stroke({ width: 2, color: COLORS.selected })
  }
}

/**
 * Moves a thing being dragged to where it may stand, right away: its room, or the common
 * floor, is repainted, its name follows and a door opens its wall where it goes. The new
 * place is returned, to be saved when it is dropped.
 */
function carryTo(view: View, roomId: string, target: Target, x: number, y: number): { x: number; y: number } | undefined {
  const layout = view.layout
  if (!layout) return undefined
  if (roomId === BUILDING) {
    if (target.kind !== 'piece') return undefined
    const moved = placeCommon(floorArrangementOf(layout), target.index, x, y).furniture[target.index]
    layout.hall[target.index] = { ...layout.hall[target.index], x: moved.x + layout.floor.x * TILE, y: moved.y + layout.floor.y * TILE }
    view.hall?.paint()
    if (view.selection) outline(view.selection, layout, { roomId, target })
    return { x: moved.x, y: moved.y }
  }

  const room = layout.rooms.find((r) => r.id === roomId)
  if (!room) return undefined
  const moved = place(arrangementOf(room), target, x, y)
  let spot = { x, y }
  if (target.kind === 'desk') {
    spot = moved.desks[target.id]
    const desk = room.desks.find((d) => d.id === target.id)!
    desk.x = room.x * TILE + spot.x
    desk.y = room.y * TILE + spot.y
    view.names?.get(desk.id)?.position.set(desk.x + STATION.width / 2, desk.y + STATION.height + 1)
  } else if (target.kind === 'piece') {
    spot = moved.furniture[target.index]
    room.props[target.index] = { ...room.props[target.index], x: spot.x, y: spot.y }
  } else if (target.kind === 'window') {
    spot = { x: moved.windows[target.index], y: WINDOW.y }
    room.windows[target.index] = spot.x
  } else {
    room.door = moved.door
    layout.doors = layout.doors.map((d) => (d.roomId === room.id ? doorOf(room) : d))
    if (view.walls && view.scene) drawWalls(view.walls, layout, view.scene, themeOf(view.scene.office.theme))
  }
  view.rooms?.get(room.id)?.paint(view.tick)
  if (view.selection) outline(view.selection, layout, { roomId, target })
  return spot
}

/** The common floor: everything inside the building that is not a room, with a back wall along the north side. */
function hallFloor(layout: BuildingLayout, scene: Scene, theme: Theme): { sprite: Sprite; source: Source } {
  const colors = themeColors(theme.colors)
  const { floor } = layout
  const size = { width: floor.width * TILE, height: floor.height * TILE }
  const upper = upperWall(theme.wall)
  const pixels: Pixel[] = []
  // A back wall wherever the building starts going south, so cuts get one as the north side does.
  for (let c = 0; c < floor.width; c++) {
    let fromWall = 0
    for (let r = 0; r < floor.height; r++) {
      if (!inBuilding(layout, floor.x + c, floor.y + r)) {
        fromWall = 0
        continue
      }
      const tile = fromWall === 0 ? upper : fromWall < WALL ? theme.wall : HALL_FLOOR
      pixels.push(...pixelsOf(tile, 0, colors, c * TILE, r * TILE))
      fromWall++
    }
  }
  const lit = scene.dark ? inTheDark(pixels, hallItems(layout, theme).lights, theme.colors.night) : pixels
  const canvas = document.createElement('canvas')
  paintInto(canvas, size.width, size.height, lit)
  return { sprite: spriteOf(canvas, floor.x * TILE, floor.y * TILE), source: { canvas, x: floor.x * TILE, y: floor.y * TILE } }
}

function spriteOf(canvas: HTMLCanvasElement, x: number, y: number): Sprite {
  const texture = Texture.from(canvas)
  texture.source.scaleMode = 'nearest'
  const sprite = new Sprite(texture)
  sprite.position.set(x, y)
  return sprite
}

/** The common floor's furniture, in pixels from the building's corner, with its lights. */
function hallItems(layout: BuildingLayout, theme: Theme) {
  const size = { width: layout.floor.width * TILE, height: layout.floor.height * TILE }
  const items = layout.hall.map((p) => propItem(p.name, p.x - layout.floor.x * TILE, p.y - layout.floor.y * TILE, 0, theme, size))
  return { size, items, lights: sceneLights(items) }
}

/** What stands on the common floor: over the walls behind it, as it stands in front of them. */
function hallFurniture(layout: BuildingLayout, scene: Scene, theme: Theme): { sprite: Sprite; source: Source; painter: { paint: () => void } } {
  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, layout.floor.width * TILE)
  canvas.height = Math.max(1, layout.floor.height * TILE)
  const sprite = spriteOf(canvas, layout.floor.x * TILE, layout.floor.y * TILE)
  return {
    sprite,
    source: { canvas, x: layout.floor.x * TILE, y: layout.floor.y * TILE },
    painter: {
      paint: () => {
        const { size, items, lights } = hallItems(layout, theme)
        const pixels = scenePixels(items)
        paintInto(canvas, size.width, size.height, scene.dark ? inTheDark(pixels, lights, theme.colors.night) : pixels)
        sprite.texture.source.update()
      },
    },
  }
}

/** The building's walls, from the plan: faces, then outlines, then caps, then door jambs. */
function drawWalls(g: Graphics, layout: BuildingLayout, scene: Scene, theme: Theme) {
  const tone = (color: string) => (scene.dark ? unlit(color, theme.colors.night) : color)
  const plan = wallPlan(layout)
  g.clear()
  for (const f of plan.faces) {
    g.rect(f.x, f.y, f.width, f.height).fill(tone(theme.colors.outline))
    g.rect(f.x + 1, f.y, f.width - 2, f.height - 2).fill(tone(theme.colors['wall.light']))
    g.rect(f.x + 1, f.y + f.height - 3, f.width - 2, 1).fill(tone(theme.colors['wall.shadow']))
  }
  // Every outline first, then every cap, so walls meet without seams.
  for (const s of plan.caps) g.rect(s.x, s.y, s.width, s.height).fill(tone(theme.colors.outline))
  for (const s of plan.caps) g.rect(s.x + 1, s.y + 1, s.width - 2, s.height - 2).fill(tone(theme.colors['wall.top']))
  // Jambs: the door frame stands a little proud of the wall.
  for (const j of plan.jambs) {
    g.rect(j.x, j.y, j.width, j.height).fill(tone(theme.colors.outline))
    g.rect(j.x + 1, j.y + 1, j.width - 2, j.height - 2).fill(tone(theme.colors['trim.light']))
  }
}

/**
 * What a window or a doorway in a back wall shows: the sky when the wall is the building's
 * own, otherwise whatever is painted just behind it.
 */
function behind(room: RoomBox, layout: BuildingLayout, sources: Source[], x: number, width: number, height: number, tick: number, phase: DayPhase, weather: Sky | undefined): { pixels: Pixel[]; outside: boolean } {
  const worldX = room.x * TILE + x
  if (!inBuilding(layout, Math.floor((worldX + width / 2) / TILE), room.y - 1)) return { pixels: skyPixels(worldX, width, height, tick, phase, weather), outside: true }
  return { pixels: sample(sources, worldX, room.y * TILE - height, width, height), outside: false }
}

/** One room: its pixel art in a sprite, between the common floor and the walls. Windows look through it. */
function drawRoom(room: RoomBox, layout: BuildingLayout, scene: Scene, theme: Theme, world: Container, sources: Source[]): RoomPainter {
  const size = { width: room.width * TILE, height: room.height * TILE }
  const shell = roomShell(theme, room.width, room.height - WALL_HEIGHT / TILE)
  const canvas = document.createElement('canvas')
  paintInto(canvas, size.width, size.height, shell)
  const sprite = spriteOf(canvas, room.x * TILE, room.y * TILE)
  world.addChild(sprite)
  sources.push({ canvas, x: room.x * TILE, y: room.y * TILE })
  const colors = themeColors(theme.colors)
  // Some moods keep the lights off whatever the hour.
  const dark = scene.dark || Boolean(theme.dark)

  const views = new Map(scene.office.rooms.flatMap((r) => r.desks).map((d) => [d.id, d]))
  const looks = new Map(room.desks.map((d) => [d.id, lookOf(views.get(d.id) ?? d)]))
  const states = new Map(room.desks.map((d) => [d.id, scene.states?.get(d.id) ?? agentStateOf(d.id, scene.tasks, scene.activities.get(d.id))]))
  return {
    paint: (tick) => {
      const now = Date.now()
      const phase = phaseOf(new Date(now).getHours())
      const openings: SceneItem[] = room.windows.map((x) => {
        const view = behind(room, layout, sources, x + GLASS.x, GLASS.width, GLASS.height, tick, phase, scene.weather)
        const lights: Light[] = view.outside && dark ? windowLight(phase, scene.weather, tick) : []
        return { x, y: WINDOW.y, height: WINDOW_SIZE.height, pixels: windowPixels(view.pixels, colors), layer: 'wall', lights }
      })
      if (room.door.wall === 'Back') {
        const view = behind(room, layout, sources, room.door.offset * TILE + 3, 26, 27, tick, phase, scene.weather)
        openings.push({ x: room.door.offset * TILE, y: 0, height: WALL_HEIGHT, pixels: doorwayPixels(view.pixels, colors), layer: 'wall' })
      }
      const items = [
        ...openings,
        ...room.props.map((p) => propItem(p.name, p.x, p.y, tick, theme, size)),
        ...room.desks.flatMap((d, i) => {
          const away = scene.walks?.at(d.id, now)
          const desk = deskItem(
            looks.get(d.id)!, states.get(d.id)!, d.x - room.x * TILE, d.y - room.y * TILE, tick + i * 7, theme, undefined,
            energyOf(scene.contexts?.get(d.id), states.get(d.id)!, now), Boolean(away),
          )
          return away ? [desk, ...walkerItems(looks.get(d.id)!, away, room.x * TILE, room.y * TILE)] : [desk]
        }),
        ...room.desks.flatMap((d, i) =>
          internItems(looks.get(d.id)!, scene.interns?.at(d.id, now) ?? [], d.x - room.x * TILE, d.y - room.y * TILE, tick + i * 7),
        ),
      ]
      paintInto(canvas, size.width, size.height, roomPixels(theme, shell, items, dark))
      sprite.texture.source.update()
    },
  }
}

/** The words of a bubble, wrapped to its width; a long word breaks rather than spill out. */
function bubbleWords(activity: DeskActivity): Text {
  const text = new Text({
    text: bubbleText(activity.headline),
    style: {
      fontFamily: 'IBM Plex Sans',
      fontSize: 7,
      lineHeight: 9,
      fill: COLORS.bubbleText,
      wordWrap: true,
      breakWords: true,
      wordWrapWidth: BUBBLE_WIDTH - 8,
    },
  })
  text.resolution = 6
  return text
}

/**
 * A speech bubble placed above its desk, with a tail down to (anchorX, anchorY). A waiting
 * agent's bubble is amber with a dark frame: told apart by its frame and its question too.
 */
function bubble(activity: DeskActivity, text: Text, spot: PlacedBubble): Container {
  const waiting = activity.kind === 'Waiting'
  const fill = waiting ? COLORS.waiting : activity.kind === 'Stopped' ? COLORS.mute : COLORS.bubble
  const { left, top, width, height, anchorX: x, anchorY: y } = spot
  const bottom = top + height

  const shape = new Graphics()
  shape.roundRect(left, top, width, height, 3).fill(fill)
  if (waiting) shape.roundRect(left, top, width, height, 3).stroke({ width: 1, color: COLORS.roomEdge })
  // A tail that grows when the bubble had to climb above a neighbour's.
  const tailX = Math.max(left + 4, Math.min(left + width - 4, x))
  shape.poly([tailX - 3, bottom, tailX + 3, bottom, x, y]).fill(fill)
  text.position.set(left + 4, top + 3)

  const group = new Container()
  group.addChild(shape, text)
  return group
}

/** Desk names fit their slot; longer ones end with an ellipsis. */
const NAME_WIDTH = 42

function label(text: string, x: number, y: number, fill: number, size: number, centered = false, maxWidth = Infinity): Text {
  // A dark outline keeps names readable on light walls and floors too.
  const t = new Text({ text, style: { fontFamily: 'Silkscreen', fontSize: size, fill, stroke: { color: COLORS.roomEdge, width: 2, join: 'round' } } })
  for (let n = text.length - 1; t.width > maxWidth && n > 1; n--) t.text = `${text.slice(0, n).trimEnd()}…`
  t.resolution = 4
  if (centered) t.anchor.set(0.5, 0)
  t.position.set(x, y)
  return t
}
