import { useEffect, useRef, type ReactNode } from 'react'
import { Maximize } from 'lucide-react'
import { Application, Container, Graphics, Sprite, Text, Texture } from 'pixi.js'
import type { DeskActivity, OfficeView, WorkTaskView } from '../api/types'
import { paintInto } from '../art/canvas'
import { deskItem, propItem, roomPixels, roomShell, WALL_HEIGHT } from '../art/roomScene'
import { STATION } from '../art/station'
import { themeOf } from '../art/themes'
import { agentStateOf, appearanceOf } from './agents'
import { between, fit, pan, toWorld, wheelFactor, zoomAt, type Camera } from './camera'
import { bestLayout, layoutOffice, TILE, type BuildingLayout, type RoomBox } from './layout'

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
}

/** Draws the selected office as a pixel-art floor plan you can zoom, pan and fly through. */
export function BuildingCanvas({ office, tasks, activities, dark, controls }: Props) {
  const host = useRef<HTMLDivElement>(null)
  const appRef = useRef<Application | null>(null)
  const draw = useRef<() => void>(() => {})
  const fitAll = useRef<() => void>(() => {})

  // Refreshed after every render so the canvas callbacks always see the latest props.
  useEffect(() => {
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
      const { world, rooms } = drawBuilding(layout, { office, tasks, activities, dark })
      app.stage.addChild(world)
      view.world = world
      view.rooms = rooms
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
      fitAll.current = () => {
        const view = viewOf(app)
        view.auto = true
        if (view.layout) flyTo(view, fit(worldRect(view.layout), app.screen))
      }
      listeners.push(...attachNavigation(app))
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

  useEffect(() => draw.current(), [office, tasks, activities, dark])

  return (
    <div className="building-canvas" ref={host}>
      <div className="building-controls">
        <span className="hint">Scroll to zoom · drag to move · double-click a room</span>
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
  rooms?: RoomPainter[]
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
function attachNavigation(app: Application): (() => void)[] {
  const canvas = app.canvas
  const view = viewOf(app)
  const point = (e: MouseEvent) => {
    const box = canvas.getBoundingClientRect()
    return { x: e.clientX - box.left, y: e.clientY - box.top }
  }
  let drag: { x: number; y: number; id: number } | undefined

  const onWheel = (e: WheelEvent) => {
    e.preventDefault()
    if (view.animation) cancelAnimationFrame(view.animation)
    view.auto = false
    view.camera = zoomAt(view.camera, wheelFactor(e.deltaY), point(e))
    apply(view)
  }
  const onDown = (e: PointerEvent) => {
    if (e.button !== 0) return
    drag = { ...point(e), id: e.pointerId }
    try {
      // Keeps the drag going when the pointer leaves the canvas.
      canvas.setPointerCapture(e.pointerId)
    } catch {
      // Some pointers cannot be captured; dragging still works inside the canvas.
    }
    canvas.style.cursor = 'grabbing'
  }
  const onMove = (e: PointerEvent) => {
    if (!drag || drag.id !== e.pointerId) return
    const now = point(e)
    if (view.animation) cancelAnimationFrame(view.animation)
    view.auto = false
    view.camera = pan(view.camera, now.x - drag.x, now.y - drag.y)
    drag = { ...now, id: e.pointerId }
    apply(view)
  }
  const onUp = (e: PointerEvent) => {
    if (drag?.id === e.pointerId) drag = undefined
    canvas.style.cursor = 'grab'
  }
  const onDoubleClick = (e: MouseEvent) => {
    if (!view.layout) return
    const world = toWorld(view.camera, point(e))
    const room = view.layout.rooms.find(
      (r) => world.x >= r.x * TILE && world.x < (r.x + r.width) * TILE && world.y >= r.y * TILE && world.y < (r.y + r.height) * TILE,
    )
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

function drawBuilding(layout: BuildingLayout, scene: Scene): { world: Container; rooms: RoomPainter[] } {
  const world = new Container()
  const labels = new Container()
  const bubbles = new Container()
  const rooms = layout.rooms.map((room) => drawRoom(room, scene, world))

  for (const room of layout.rooms) {
    labels.addChild(label(room.name, room.x * TILE + 6, room.y * TILE + 4, COLORS.label, 8))
    for (const desk of room.desks) {
      labels.addChild(label(desk.name, desk.x + STATION.width / 2, desk.y + STATION.height + 1, COLORS.mute, 6, true, NAME_WIDTH))
      const activity = scene.activities.get(desk.id)
      if (activity?.headline) bubbles.addChild(bubble(activity, desk.x + STATION.width / 2, desk.y - 1))
    }
  }

  // Names, then bubbles, go on top of every room, so they can spill over walls.
  world.addChild(labels, bubbles)
  return { world, rooms }
}

/** One room: its pixel art in a sprite, with a dark edge so rooms stand apart. */
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

  const edge = new Graphics()
  edge.rect(room.x * TILE - 1, room.y * TILE - 1, size.width + 2, size.height + 2).stroke({ width: 1, color: COLORS.roomEdge })
  world.addChild(edge, sprite)

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
