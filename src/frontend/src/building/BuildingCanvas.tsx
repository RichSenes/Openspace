import { useEffect, useRef, type ReactNode } from 'react'
import { Maximize } from 'lucide-react'
import { Application, Container, Graphics, Sprite, Text, Texture } from 'pixi.js'
import type { DeskActivity, OfficeView, WorkTaskView } from '../api/types'
import { paintInto, toCanvas } from '../art/canvas'
import { inTheDark, unlit } from '../art/lighting'
import { deskItem, propItem, roomPixels, roomShell, WALL_HEIGHT } from '../art/roomScene'
import { sceneLights, scenePixels } from '../art/scene'
import { HALL_FLOOR } from '../art/sheets/rooms'
import { pixelsOf, type Pixel } from '../art/sprite'
import { STATION, themeColors } from '../art/station'
import { themeOf } from '../art/themes'
import { agentStateOf, appearanceOf } from './agents'
import { arrangementOf, boundsOf, handlesOf, place, targetAt, type Edge, type Target } from './arrange'
import { between, fit, pan, toWorld, wheelFactor, zoomAt, type Camera } from './camera'
import { bestLayout, layoutOffice, TILE, WALL, type BuildingLayout, type RoomBox } from './layout'

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
  /** Lights off: only screens, lamps and other glowing things light the rooms. */
  dark: boolean
  /** More controls beside Fit. */
  controls?: ReactNode
  /** Present while the person arranges rooms: things can then be picked up and moved. */
  arrange?: ArrangeControls
}

/** A room being arranged and, possibly, the desk or piece of furniture picked in it. */
export interface Selection {
  roomId: string
  target?: Target
}

export interface ArrangeControls {
  selection: Selection | null
  onSelect: (selection: Selection | null) => void
  /** A target was dropped at (x, y), in pixels from its room's corner. */
  onPlace: (roomId: string, target: Target, x: number, y: number) => void
  /** A room's edge was dragged: its new size in tiles, wall to wall and floor depth. */
  onResize: (roomId: string, width: number, depth: number) => void
}

const EDGE_CURSORS: Record<Edge, string> = { right: 'ew-resize', bottom: 'ns-resize', corner: 'nwse-resize' }

/** Draws the selected office as a pixel-art floor plan you can zoom, pan and fly through. */
export function BuildingCanvas({ office, tasks, activities, dark, controls, arrange }: Props) {
  const host = useRef<HTMLDivElement>(null)
  const appRef = useRef<Application | null>(null)
  const draw = useRef<() => void>(() => {})
  const fitAll = useRef<() => void>(() => {})
  const arrangeRef = useRef(arrange)

  // Refreshed after every render so the canvas callbacks always see the latest props.
  useEffect(() => {
    arrangeRef.current = arrange
    draw.current = () => {
      const app = appRef.current
      if (!app) return
      const view = viewOf(app)
      const auto = view.auto
      const layout = auto
        ? bestLayout(office, { width: Math.max(1, app.screen.width - 32), height: Math.max(1, app.screen.height - 32) }).layout
        : layoutOffice(office, view.layout?.width ?? Number.MAX_SAFE_INTEGER)
      view.layout = layout

      app.stage.removeChildren().forEach((child) => child.destroy({ children: true, texture: true, textureSource: true }))
      const { world, rooms, names, selection } = drawBuilding(layout, { office, tasks, activities, dark }, arrange?.selection)
      app.stage.addChild(world)
      view.world = world
      view.rooms = rooms
      view.names = names
      view.selection = selection
      rooms.forEach((room) => room.paint(view.tick))
      if (auto) view.camera = fit(worldRect(layout), app.screen)
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
      app.renderer.on('resize', () => draw.current())
      // Pixi follows the window; the panel beside the building changes its size too.
      const observer = new ResizeObserver(() => app.resize())
      observer.observe(element)
      listeners.push(() => observer.disconnect())
      fitAll.current = () => {
        const view = viewOf(app)
        view.auto = true
        if (view.layout) flyTo(view, fit(worldRect(view.layout), app.screen))
      }
      listeners.push(...attachNavigation(app, () => arrangeRef.current))
      draw.current()
      // The art moves at its own pace; people who prefer less motion get still rooms.
      if (!reducedMotion()) {
        const timer = setInterval(() => {
          const view = viewOf(app)
          view.tick++
          view.rooms?.forEach((room) => room.paint(view.tick))
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
  useEffect(() => draw.current(), [office, tasks, activities, dark, arranging, arrange?.selection])

  return (
    <div className="building-canvas" ref={host}>
      <div className="building-controls">
        <span className="hint">
          {arrange ? 'Drag desks and furniture · arrows nudge · Delete removes' : 'Scroll to zoom · drag to move · double-click a room'}
        </span>
        <button type="button" className="btn" onClick={() => fitAll.current()}>
          <Maximize size={16} /> Fit
        </button>
        {controls}
      </div>
    </div>
  )
}

/** Camera, layout and animation of one canvas, kept beside the Pixi application. */
interface View {
  camera: Camera
  /** True until the person zooms or pans: the view then follows the window and the data. */
  auto: boolean
  layout?: BuildingLayout
  world?: Container
  rooms?: Map<string, RoomPainter>
  /** Desk name labels, by desk id: they follow a desk being dragged. */
  names?: Map<string, Text>
  /** The outline of what is selected while arranging. */
  selection?: Graphics
  /** Frames of pixel art shown so far. */
  tick: number
  animation?: number
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

/** Wheel to zoom at the cursor, left-drag to pan, double-click to fly into a room. Returns the cleanups. */
function attachNavigation(app: Application, arrange: () => ArrangeControls | undefined): (() => void)[] {
  const canvas = app.canvas
  const view = viewOf(app)
  const point = (e: MouseEvent) => {
    const box = canvas.getBoundingClientRect()
    return { x: e.clientX - box.left, y: e.clientY - box.top }
  }
  let drag: { x: number; y: number; id: number; moved: number } | undefined
  let carry: { roomId: string; target: Target; from: { x: number; y: number }; grab: { x: number; y: number }; to?: { x: number; y: number } } | undefined
  let stretch: { roomId: string; edge: Edge; size: { width: number; depth: number } } | undefined
  // Rooms are redrawn as soon as something is picked: always look them up by id.
  const roomById = (id: string) => view.layout?.rooms.find((r) => r.id === id)

  const roomAt = (world: { x: number; y: number }) =>
    view.layout?.rooms.find(
      (r) => world.x >= r.x * TILE && world.x < (r.x + r.width) * TILE && world.y >= r.y * TILE && world.y < (r.y + r.height) * TILE,
    )
  /** The room and the thing under the pointer, in pixels from that room's corner. */
  const pick = (e: MouseEvent) => {
    const world = toWorld(view.camera, point(e))
    const room = roomAt(world)
    if (!room) return undefined
    const local = { x: world.x - room.x * TILE, y: world.y - room.y * TILE }
    return { room, local, target: targetAt(arrangementOf(room), local) }
  }
  /** The edge handle of the selected room under the pointer, a few screen pixels around it. */
  const handleAt = (e: MouseEvent) => {
    const id = arrange()?.selection?.roomId
    const room = id ? roomById(id) : undefined
    if (!room) return undefined
    const at = point(e)
    const handle = handlesOf(room).find((h) => {
      const x = h.x * view.camera.scale + view.camera.x
      const y = h.y * view.camera.scale + view.camera.y
      return Math.abs(at.x - x) <= 10 && Math.abs(at.y - y) <= 10
    })
    return handle && { room, edge: handle.edge }
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
    const handle = controls ? handleAt(e) : undefined
    const picked = controls && !handle ? pick(e) : undefined
    if (handle) {
      // The room grows under the pointer: the camera stays put meanwhile.
      view.auto = false
      stretch = { roomId: handle.room.id, edge: handle.edge, size: { width: handle.room.width, depth: handle.room.height - WALL } }
    } else if (controls && picked?.target) {
      const box = boundsOf(arrangementOf(picked.room), picked.target)!
      carry = { roomId: picked.room.id, target: picked.target, from: { x: box.x, y: box.y }, grab: picked.local }
      controls.onSelect({ roomId: picked.room.id, target: picked.target })
    }
    drag = { ...point(e), id: e.pointerId, moved: 0 }
    try {
      // Keeps the drag going when the pointer leaves the canvas.
      canvas.setPointerCapture(e.pointerId)
    } catch {
      // Some pointers cannot be captured; dragging still works inside the canvas.
    }
    canvas.style.cursor = stretch ? EDGE_CURSORS[stretch.edge] : carry ? 'move' : 'grabbing'
  }
  const onMove = (e: PointerEvent) => {
    if (!drag) {
      if (arrange()) {
        const handle = handleAt(e)
        canvas.style.cursor = handle ? EDGE_CURSORS[handle.edge] : pick(e)?.target ? 'move' : 'grab'
      }
      return
    }
    if (drag.id !== e.pointerId) return
    const now = point(e)
    drag.moved += Math.abs(now.x - drag.x) + Math.abs(now.y - drag.y)
    const room = carry ? roomById(carry.roomId) : stretch ? roomById(stretch.roomId) : undefined
    if (carry && room) {
      const world = toWorld(view.camera, now)
      const local = { x: world.x - room.x * TILE, y: world.y - room.y * TILE }
      carry.to = carryTo(view, room, carry.target, carry.from.x + local.x - carry.grab.x, carry.from.y + local.y - carry.grab.y)
      drag = { ...now, id: e.pointerId, moved: drag.moved }
      return
    }
    if (stretch && room) {
      const world = toWorld(view.camera, now)
      const size = {
        width: stretch.edge === 'bottom' ? stretch.size.width : Math.round((world.x - room.x * TILE) / TILE),
        depth: stretch.edge === 'right' ? stretch.size.depth : Math.round((world.y - room.y * TILE) / TILE) - WALL,
      }
      // Whole tiles only: the room is redrawn when it gains or loses one.
      if (size.width !== stretch.size.width || size.depth !== stretch.size.depth) {
        stretch.size = size
        arrange()?.onResize(stretch.roomId, size.width, size.depth)
      }
      drag = { ...now, id: e.pointerId, moved: drag.moved }
      return
    }
    if (view.animation) cancelAnimationFrame(view.animation)
    view.auto = false
    view.camera = pan(view.camera, now.x - drag.x, now.y - drag.y)
    drag = { ...now, id: e.pointerId, moved: drag.moved }
    apply(view)
  }
  const onUp = (e: PointerEvent) => {
    if (drag?.id !== e.pointerId) return
    const controls = arrange()
    if (carry?.to) controls?.onPlace(carry.roomId, carry.target, carry.to.x, carry.to.y)
    // A click on a room, not a drag of the view, picks the room to arrange.
    else if (controls && !carry && !stretch && drag.moved < 4) {
      const room = pick(e)?.room
      controls.onSelect(room ? { roomId: room.id } : null)
    }
    drag = undefined
    carry = undefined
    stretch = undefined
    canvas.style.cursor = 'grab'
  }
  const onDoubleClick = (e: MouseEvent) => {
    if (!view.layout) return
    const world = toWorld(view.camera, point(e))
    const room = roomAt(world)
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
  dark: boolean
}

function drawBuilding(
  layout: BuildingLayout,
  scene: Scene,
  selected: Selection | null | undefined,
): { world: Container; rooms: Map<string, RoomPainter>; names: Map<string, Text>; selection: Graphics } {
  const world = new Container()
  const labels = new Container()
  const bubbles = new Container()
  world.addChild(hallFloor(layout, scene))
  const rooms = new Map(layout.rooms.map((room) => [room.id, drawRoom(room, scene, world)]))
  world.addChild(walls(layout, scene), hallFurniture(layout, scene))
  const names = new Map<string, Text>()

  for (const room of layout.rooms) {
    labels.addChild(label(room.name, room.x * TILE + 6, room.y * TILE + 4, COLORS.label, 8))
    for (const desk of room.desks) {
      const name = label(desk.name, desk.x + STATION.width / 2, desk.y + STATION.height + 1, COLORS.mute, 6, true, NAME_WIDTH)
      names.set(desk.id, name)
      labels.addChild(name)
      const activity = scene.activities.get(desk.id)
      // Bubbles would hide what is being arranged.
      if (activity?.headline && selected === undefined) bubbles.addChild(bubble(activity, desk.x + STATION.width / 2, desk.y - 1))
    }
  }

  const selection = new Graphics()
  outline(selection, layout, selected)
  // Names, then bubbles, go on top of every room, so they can spill over walls.
  world.addChild(labels, bubbles, selection)
  return { world, rooms, names, selection }
}

/** Frames the room being arranged and, inside it, the thing picked. */
function outline(g: Graphics, layout: BuildingLayout, selected: Selection | null | undefined) {
  g.clear()
  const room = selected && layout.rooms.find((r) => r.id === selected.roomId)
  if (!room) return
  g.rect(room.x * TILE - 3, room.y * TILE - 3, room.width * TILE + 6, room.height * TILE + 6).stroke({ width: 2, color: COLORS.selected })
  // Square handles to drag the walls out or in.
  for (const h of handlesOf(room)) g.rect(h.x - 3, h.y - 3, 6, 6).fill(COLORS.selected).stroke({ width: 1, color: COLORS.roomEdge })
  const box = selected.target && boundsOf(arrangementOf(room), selected.target)
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
 * Moves a thing being dragged to where it may stand, right away: its room is repainted
 * and its name follows. The new place is returned, to be saved when it is dropped.
 */
function carryTo(view: View, room: RoomBox, target: Target, x: number, y: number): { x: number; y: number } {
  const moved = place(arrangementOf(room), target, x, y)
  const spot = target.kind === 'desk' ? moved.desks[target.id] : moved.furniture[target.index]
  if (target.kind === 'desk') {
    const desk = room.desks.find((d) => d.id === target.id)!
    desk.x = room.x * TILE + spot.x
    desk.y = room.y * TILE + spot.y
    view.names?.get(desk.id)?.position.set(desk.x + STATION.width / 2, desk.y + STATION.height + 1)
  } else {
    room.props[target.index] = { ...room.props[target.index], x: spot.x, y: spot.y }
  }
  view.rooms?.get(room.id)?.paint(view.tick)
  if (view.selection && view.layout) outline(view.selection, view.layout, { roomId: room.id, target })
  return { x: spot.x, y: spot.y }
}

/** A still picture of pixels, `width` by `height`, at (x, y) in the world. */
function still(pixels: Pixel[], x: number, y: number, width: number, height: number): Sprite {
  const texture = Texture.from(toCanvas(width, height, pixels))
  texture.source.scaleMode = 'nearest'
  const sprite = new Sprite(texture)
  sprite.position.set(x, y)
  return sprite
}

/** The corridor furniture, in pixels from the building's corner, with its lights. */
function hallItems(layout: BuildingLayout, scene: Scene) {
  const theme = themeOf(scene.office.theme)
  const size = { width: layout.floor.width * TILE, height: layout.floor.height * TILE }
  const items = layout.hall.map((p) => propItem(p.name, p.x - layout.floor.x * TILE, p.y - layout.floor.y * TILE, 0, theme, size))
  return { theme, size, items, lights: sceneLights(items) }
}

/** The common floor: everything inside the building that is not a room. Rooms cover the rest. */
function hallFloor(layout: BuildingLayout, scene: Scene): Sprite {
  const { theme, size, lights } = hallItems(layout, scene)
  const colors = themeColors(theme.colors)
  const pixels: Pixel[] = []
  for (let r = 0; r < layout.floor.height; r++)
    for (let c = 0; c < layout.floor.width; c++) pixels.push(...pixelsOf(HALL_FLOOR, 0, colors, c * TILE, r * TILE))
  const lit = scene.dark ? inTheDark(pixels, lights, theme.colors.night) : pixels
  return still(lit, layout.floor.x * TILE, layout.floor.y * TILE, size.width, size.height)
}

/** What stands in the corridors: over the walls behind it, as it stands in front of them. */
function hallFurniture(layout: BuildingLayout, scene: Scene): Sprite {
  const { theme, size, items, lights } = hallItems(layout, scene)
  const pixels = scenePixels(items)
  return still(scene.dark ? inTheDark(pixels, lights, theme.colors.night) : pixels, layout.floor.x * TILE, layout.floor.y * TILE, size.width, size.height)
}

/** Walls seen from above are a thin cap: this thick, centred on the line between two spaces. */
const WALL_CAP = 4
const OUTER_CAP = 6
/** How much of a front wall shows below its cap, on the corridor side. */
const WALL_FACE = 7

/**
 * The building's walls: around it, between rooms side by side and along the front of
 * each room, open where its door is.
 */
function walls(layout: BuildingLayout, scene: Scene): Graphics {
  const theme = themeOf(scene.office.theme)
  const tone = (color: string) => (scene.dark ? unlit(color, theme.colors.night) : color)
  const half = WALL_CAP / 2
  const strips: { x: number; y: number; width: number; height: number }[] = []
  // The side of a front wall that faces the corridor, seen from above and in front.
  const faces: { x: number; y: number; width: number }[] = []
  const line = (x1: number, y1: number, x2: number, y2: number, cap = WALL_CAP) =>
    strips.push({ x: Math.min(x1, x2) - cap / 2, y: Math.min(y1, y2) - cap / 2, width: Math.abs(x2 - x1) + cap, height: Math.abs(y2 - y1) + cap })
  const front = (x1: number, x2: number, y: number) => {
    line(x1, y, x2, y)
    faces.push({ x: x1 - half, y: y + half, width: x2 - x1 + WALL_CAP })
  }

  const { floor } = layout
  // The outer walls are thicker than the ones between rooms.
  const [west, north, east, south] = [floor.x * TILE, floor.y * TILE, (floor.x + floor.width) * TILE, (floor.y + floor.height) * TILE]
  line(west, north, east, north, OUTER_CAP)
  line(west, north, west, south, OUTER_CAP)
  line(east, north, east, south, OUTER_CAP)
  line(west, south, east, south, OUTER_CAP)
  for (const room of layout.rooms) {
    const [left, top, right, bottom] = [room.x * TILE, room.y * TILE, (room.x + room.width) * TILE, (room.y + room.height) * TILE]
    line(left, top, right, top)
    line(left, top, left, bottom)
    line(right, top, right, bottom)
    const door = layout.doors.find((d) => d.roomId === room.id)
    if (!door) front(left, right, bottom)
    else {
      front(left, door.x - half, bottom)
      front(door.x + door.width + half, right, bottom)
    }
  }

  // Every outline first, then every cap, so walls meet without seams.
  const g = new Graphics()
  for (const f of faces) {
    g.rect(f.x, f.y, f.width, WALL_FACE).fill(tone(theme.colors.outline))
    g.rect(f.x + 1, f.y, f.width - 2, WALL_FACE - 2).fill(tone(theme.colors['wall.light']))
    g.rect(f.x + 1, f.y + WALL_FACE - 3, f.width - 2, 1).fill(tone(theme.colors['wall.shadow']))
  }
  for (const s of strips) g.rect(s.x, s.y, s.width, s.height).fill(tone(theme.colors.outline))
  for (const s of strips) g.rect(s.x + 1, s.y + 1, s.width - 2, s.height - 2).fill(tone(theme.colors['wall.top']))
  // Jambs: the door frame stands a little proud of the wall.
  for (const d of layout.doors)
    for (const x of [d.x - half - 1, d.x + d.width - half - 1]) {
      g.rect(x, d.y - 4, WALL_CAP + 2, 8).fill(tone(theme.colors.outline))
      g.rect(x + 1, d.y - 3, WALL_CAP, 6).fill(tone(theme.colors['trim.light']))
    }
  return g
}

/** One room: its pixel art in a sprite, between the common floor and the walls. */
function drawRoom(room: RoomBox, scene: Scene, world: Container): RoomPainter {
  const theme = themeOf(scene.office.theme)
  const size = { width: room.width * TILE, height: room.height * TILE }
  const shell = roomShell(theme, room.width, room.height - WALL_HEIGHT / TILE)
  const canvas = document.createElement('canvas')
  paintInto(canvas, size.width, size.height, shell)
  const texture = Texture.from(canvas)
  texture.source.scaleMode = 'nearest'
  const sprite = new Sprite(texture)
  sprite.position.set(room.x * TILE, room.y * TILE)

  world.addChild(sprite)

  const looks = new Map(room.desks.map((d) => [d.id, appearanceOf(d.id)]))
  const states = new Map(room.desks.map((d) => [d.id, agentStateOf(d.id, scene.tasks, scene.activities.get(d.id))]))
  return {
    paint: (tick) => {
      const items = [
        ...room.props.map((p) => propItem(p.name, p.x, p.y, tick, theme, size)),
        ...room.desks.map((d, i) =>
          deskItem(looks.get(d.id)!, states.get(d.id)!, d.x - room.x * TILE, d.y - room.y * TILE, tick + i * 7, theme),
        ),
      ]
      paintInto(canvas, size.width, size.height, roomPixels(theme, shell, items, scene.dark))
      texture.source.update()
    },
  }
}

/** A speech bubble whose tail points at (x, y). */
function bubble(activity: DeskActivity, x: number, y: number): Container {
  const waiting = activity.kind === 'Waiting'
  const text = new Text({
    text: waiting ? `${activity.headline} · needs your OK` : activity.headline,
    style: {
      fontFamily: 'IBM Plex Sans',
      fontSize: 7,
      lineHeight: 9,
      fill: COLORS.bubbleText,
      wordWrap: true,
      wordWrapWidth: BUBBLE_WIDTH - 8,
    },
  })
  text.resolution = 6

  const width = Math.min(BUBBLE_WIDTH, Math.ceil(text.width) + 8)
  const height = Math.ceil(text.height) + 6
  const left = Math.round(x - width / 2)
  const top = Math.round(y - 4 - height)
  const fill = waiting ? COLORS.waiting : activity.kind === 'Stopped' ? COLORS.mute : COLORS.bubble

  const shape = new Graphics()
  shape.roundRect(left, top, width, height, 3).fill(fill)
  shape.poly([x - 3, top + height, x + 3, top + height, x, y]).fill(fill)
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
