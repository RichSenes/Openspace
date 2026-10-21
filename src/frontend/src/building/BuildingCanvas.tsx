import { useEffect, useRef } from 'react'
import { Maximize } from 'lucide-react'
import { Application, Container, Graphics, Text } from 'pixi.js'
import type { DeskActivity, OfficeView } from '../api/types'
import { between, fit, pan, toWorld, wheelFactor, zoomAt, type Camera } from './camera'
import { bestLayout, layoutOffice, TILE, type BuildingLayout } from './layout'

const COLORS = {
  ground: 0x14161c,
  floorLight: 0x2a2d38,
  floorDark: 0x252833,
  wall: 0x4a5163,
  wallTop: 0x5d6579,
  desk: 0x8a6a4a,
  deskEdge: 0x6b5139,
  screenOff: 0x1b1e26,
  screenOn: 0x8fd282,
  label: 0xece8df,
  mute: 0xa3a8b6,
  bubble: 0xece8df,
  bubbleText: 0x14161c,
  waiting: 0xf2b84b,
}

const BUBBLE_WIDTH = 132

interface Props {
  office: OfficeView
  /** Desks with a running task: their screens are lit. */
  busyDeskIds: ReadonlySet<string>
  /** What each desk is doing, by desk id: shown as speech bubbles. */
  activities: ReadonlyMap<string, DeskActivity>
}

/** Draws the selected office as a pixel-art floor plan you can zoom, pan and fly through. */
export function BuildingCanvas({ office, busyDeskIds, activities }: Props) {
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

      app.stage.removeChildren().forEach((child) => child.destroy({ children: true }))
      const world = drawBuilding(layout, busyDeskIds, activities)
      app.stage.addChild(world)
      view.world = world
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

  useEffect(() => draw.current(), [office, busyDeskIds, activities])

  return (
    <div className="building-canvas" ref={host}>
      <div className="building-controls">
        <span className="hint">Scroll to zoom · drag to move · double-click a room</span>
        <button type="button" className="btn" onClick={() => fitAll.current()}>
          <Maximize size={16} /> Fit
        </button>
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
  animation?: number
}

const views = new WeakMap<Application, View>()

function viewOf(app: Application): View {
  let view = views.get(app)
  if (!view) {
    view = { camera: { x: 0, y: 0, scale: 1 }, auto: true }
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

function drawBuilding(
  layout: BuildingLayout,
  busy: ReadonlySet<string>,
  activities: ReadonlyMap<string, DeskActivity>,
): Container {
  const world = new Container()
  const bubbles = new Container()

  for (const room of layout.rooms) {
    const g = new Graphics()
    const px = room.x * TILE
    const py = room.y * TILE

    // Walls, then a checkered floor inside them.
    g.rect(px, py, room.width * TILE, room.height * TILE).fill(COLORS.wall)
    g.rect(px, py, room.width * TILE, 4).fill(COLORS.wallTop)
    for (let ty = 2; ty < room.height - 1; ty++) {
      for (let tx = 1; tx < room.width - 1; tx++) {
        g.rect(px + tx * TILE, py + ty * TILE, TILE, TILE).fill((tx + ty) % 2 === 0 ? COLORS.floorLight : COLORS.floorDark)
      }
    }

    for (const desk of room.desks) {
      const dx = desk.x * TILE
      const dy = desk.y * TILE
      g.rect(dx - 4, dy + 10, 2 * TILE + 8, TILE - 6).fill(COLORS.deskEdge)
      g.rect(dx - 4, dy + 8, 2 * TILE + 8, TILE - 8).fill(COLORS.desk)
      g.rect(dx + 6, dy - 6, 20, 14).fill(COLORS.deskEdge)
      g.rect(dx + 8, dy - 4, 16, 10).fill(busy.has(desk.id) ? COLORS.screenOn : COLORS.screenOff)
    }
    world.addChild(g)

    world.addChild(label(room.name, px + TILE, py + TILE + 2, COLORS.label, 8))
    for (const desk of room.desks) {
      world.addChild(label(desk.name, desk.x * TILE + TILE, desk.y * TILE + TILE + 8, COLORS.mute, 8, true))
      const activity = activities.get(desk.id)
      if (activity?.headline) {
        bubbles.addChild(bubble(activity, desk.x * TILE + TILE, desk.y * TILE - 10))
      }
    }
  }

  // Bubbles go on top of every room, so they can spill over walls.
  world.addChild(bubbles)
  return world
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

function label(text: string, x: number, y: number, fill: number, size: number, centered = false): Text {
  const t = new Text({ text, style: { fontFamily: 'Silkscreen', fontSize: size, fill } })
  t.resolution = 4
  if (centered) t.anchor.set(0.5, 0)
  t.position.set(x, y)
  return t
}
