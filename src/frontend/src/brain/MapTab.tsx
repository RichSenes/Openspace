import { useEffect, useMemo, useRef, useState } from 'react'
import { Maximize } from 'lucide-react'
import { usePersistentState } from '../usePersistentState'
import { useMediaQuery } from '../ui/useMediaQuery'
import { useBrainGraph } from './api'
import { Badges } from './Badges'
import { authorsOf, bounds, rowsOf, cityLayout, createSimulation, filterGraph, foldersOfGraph, hash, nearest, neighbours, settle, step, type Simulation } from './graph'
import { drawFrame, drawOverlay, paintStars, toWorld, type Camera, type Highlight, type MapTheme, type Palette, type Scene } from './mapDraw'
import type { Graph } from './types'
import { kindsOf } from './wiki'

const ALL = '*'

/** Which token colours which kind of note; any other kind is muted. */
const KIND_TOKENS: Record<string, string> = {
  decision: '--amber',
  rule: '--danger',
  plan: '--teal',
  study: '--cyan',
  note: '--violet',
  journal: '--blue',
  session: '--st-proposed',
  digest: '--st-queued',
}

interface Props {
  current: string | undefined
  onOpen: (path: string) => void
}

/** The notes and their links, as a galaxy or a city, filtered by folder, kind and author. */
export function MapTab({ current, onOpen }: Props) {
  const graph = useBrainGraph()
  const [theme, setTheme] = usePersistentState<MapTheme>('openspace.brain.map', 'galaxy')
  const [folder, setFolder] = useState(ALL)
  const [kind, setKind] = useState('')
  const [author, setAuthor] = useState('')
  const reduced = useMediaQuery('(prefers-reduced-motion: reduce)')
  const all = graph.data
  const shown = useMemo(
    () => (all ? filterGraph(all, { folder: folder === ALL ? undefined : folder, kind: kind || undefined, author: author || undefined }) : undefined),
    [all, folder, kind, author],
  )
  const folders = useMemo(() => (all ? foldersOfGraph(all.notes) : []), [all])
  const kinds = useMemo(() => (all ? kindsOf(all.notes) : []), [all])
  const authors = useMemo(() => (all ? authorsOf(all.notes) : []), [all])
  const [fitAsked, setFitAsked] = useState(0)

  return (
    <div className="brain-map">
      <div className="brain-map-bar">
        <div className="brain-switch" role="radiogroup" aria-label="Map theme">
          {(['galaxy', 'city'] as const).map((t) => (
            <button key={t} type="button" role="radio" aria-checked={theme === t} className="brain-switch-option" onClick={() => setTheme(t)}>
              {t === 'galaxy' ? 'Galaxy' : 'City'}
            </button>
          ))}
        </div>
        <label className="field brain-map-filter">
          <span>Folder</span>
          <select className="input" value={folder} onChange={(e) => setFolder(e.target.value)}>
            <option value={ALL}>Every folder</option>
            {folders.map((f) => (
              <option key={f} value={f}>
                {f || 'Vault root'}
              </option>
            ))}
          </select>
        </label>
        <label className="field brain-map-filter">
          <span>Kind</span>
          <select className="input" value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="">Every kind</option>
            {kinds.map((k) => (
              <option key={k} value={k}>
                {k}
              </option>
            ))}
          </select>
        </label>
        <label className="field brain-map-filter">
          <span>Author</span>
          <select className="input" value={author} onChange={(e) => setAuthor(e.target.value)}>
            <option value="">Anyone</option>
            {authors.map((a) => (
              <option key={a} value={a}>
                {a}
              </option>
            ))}
          </select>
        </label>
        <button type="button" className="btn brain-map-fit" onClick={() => setFitAsked((n) => n + 1)}>
          <Maximize size={14} aria-hidden /> Fit
        </button>
        {shown && (
          <p className="hint brain-map-count" aria-live="polite">
            {shown.notes.length} notes, {shown.links.length} links
          </p>
        )}
      </div>
      {graph.isPending ? (
        <p className="hint brain-map-empty">Drawing the map…</p>
      ) : graph.error ? (
        <p className="error brain-map-empty">The brain is unreachable: {graph.error.message}</p>
      ) : !shown || shown.notes.length === 0 ? (
        <p className="hint brain-map-empty">{all?.notes.length ? 'No note fits these filters.' : 'The brain has no notes yet.'}</p>
      ) : (
        <MapCanvas key={`${theme}-${fitAsked}`} graph={shown} theme={theme} reduced={reduced} current={current} onOpen={onOpen} />
      )}
      <ul className="brain-legend" aria-label="Colours of the kinds">
        {Object.entries(KIND_TOKENS).map(([k, token]) => (
          <li key={k}>
            <span className="brain-legend-dot" style={{ background: `var(${token})` }} aria-hidden />
            {k}
          </li>
        ))}
      </ul>
    </div>
  )
}

interface Card {
  index: number
  x: number
  y: number
  /** Tapped on a touch screen: it stays, with a button to open the note. */
  pinned: boolean
}

function readPalette(el: HTMLElement): Palette {
  const css = getComputedStyle(el)
  const v = (name: string) => css.getPropertyValue(name).trim() || '#a7a3c8'
  return {
    bg: v('--bg'),
    panel: v('--panel'),
    raised: v('--raised'),
    line: v('--line'),
    edge: v('--edge-hi'),
    ink: v('--ink'),
    mute: v('--mute'),
    accent: v('--accent'),
    lit: v('--amber'),
    kinds: Object.fromEntries(Object.entries(KIND_TOKENS).map(([k, token]) => [k, v(token)])),
    other: v('--mute'),
  }
}

function MapCanvas({ graph, theme, reduced, current, onOpen }: { graph: Graph; theme: MapTheme; reduced: boolean; current: string | undefined; onOpen: (path: string) => void }) {
  const wrap = useRef<HTMLDivElement>(null)
  const canvas = useRef<HTMLCanvasElement>(null)
  const sky = useRef<HTMLCanvasElement>(null)
  const world = useRef<HTMLCanvasElement>(null)
  const [card, setCard] = useState<Card>()
  const near = useMemo(() => neighbours(graph.links), [graph.links])
  const index = useMemo(() => new Map(graph.notes.map((n, i) => [n.path, i])), [graph.notes])
  const openRef = useRef(onOpen)
  useEffect(() => {
    openRef.current = onOpen
  }, [onOpen])

  useEffect(() => {
    // Two layers: the world (turned as a whole by the compositor while the galaxy turns, so a
    // turning frame redraws nothing) and, on top, the upright titles that also take the pointer.
    const box = wrap.current
    const el = canvas.current
    const worldEl = world.current
    if (!box || !el || !worldEl) return
    const ctx = worldEl.getContext('2d')
    const top = el.getContext('2d')
    if (!ctx || !top) return
    const palette = readPalette(box)

    // The layout: a force simulation for the galaxy, blocks for the city.
    let sim: Simulation | undefined
    const scene: Scene = {
      theme,
      titles: graph.notes.map((n) => n.title),
      x: new Float64Array(0),
      y: new Float64Array(0),
      source: new Int32Array(0),
      target: new Int32Array(0),
      colors: graph.notes.map((n) => palette.kinds[n.kind ?? ''] ?? palette.other),
      seeds: graph.notes.map((n) => hash(n.path)),
    }
    if (theme === 'galaxy') {
      sim = createSimulation(graph.notes, graph.links)
      if (reduced) settle(sim)
      scene.x = sim.x
      scene.y = sim.y
      scene.source = sim.source
      scene.target = sim.target
    } else {
      const city = cityLayout(graph.notes, graph.links)
      scene.x = city.x
      scene.y = city.y
      scene.floors = city.floors
      scene.blocks = city.blocks
      scene.rows = rowsOf(city.y)
      scene.source = Int32Array.from(graph.links, (l) => index.get(l.from)!)
      scene.target = Int32Array.from(graph.links, (l) => index.get(l.to)!)
    }

    let width = 0
    let height = 0
    // The world layer is a square as wide as the view's diagonal, so turning it never shows a corner.
    let worldW = 0
    let worldH = 0
    const cam: Camera = { cx: 0, cy: 0, scale: 1, angle: 0 }
    const light: Highlight = { focus: -1, near: new Set(), current: current ? (index.get(current) ?? -1) : -1 }
    let moved = false
    const fitted = (): Camera => {
      const b = bounds(scene.x, scene.y)
      const high = theme === 'city' ? b.minY - 40 : b.minY
      const pad = 48
      const scale = Math.min((width - pad * 2) / Math.max(40, b.maxX - b.minX), (height - pad * 2) / Math.max(40, b.maxY - high))
      return { cx: (b.minX + b.maxX) / 2, cy: (high + b.maxY) / 2, scale: Math.max(0.2, Math.min(theme === 'city' ? 3 : 2.5, scale)), angle: cam.angle }
    }

    let raf = 0
    let frameMs = 0
    let drawMs = 0
    let overlayBlank = false
    let dirty = true
    // The camera the world layer was last drawn with, and when the person last panned or zoomed.
    let drawn: Camera = { ...cam }
    let handled = 0
    let slot = -1
    const started = performance.now()
    // With many notes the settling galaxy is drawn without its links, most of a frame's cost.
    const many = graph.notes.length > 150
    const linksWhileSettling = !many
    // The world is drawn without the highlight, which lives on the overlay.
    const unlit: Highlight = { focus: -1, near: new Set(), current: light.current }
    const frame = (now: number) => {
      raf = 0
      if (!width || !height) return
      const running = sim ? step(sim) : false
      if (running) dirty = true
      // Sharp again once the person stops moving the map for a moment.
      if (handled && now - handled > 180) {
        handled = 0
        dirty = true
      }
      if (!moved && dirty) Object.assign(cam, running ? lerp(cam, fitted(), 0.15) : fitted())
      const time = reduced ? 0 : now - started
      // City windows change every few seconds: only then is the city drawn again.
      const nextSlot = theme === 'city' && !reduced && !many ? Math.floor(time / 4000) : 0
      // A big city keeps its windows: redrawing it is the one costly thing here.
      if (nextSlot !== slot) {
        slot = nextSlot
        dirty = true
      }
      const t0 = performance.now()
      const redraw = dirty
      if (dirty) {
        ctx.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0)
        const calm = !running || linksWhileSettling
        drawFrame({ ctx, width: worldW, height: worldH, scene, cam: { ...cam, angle: 0 }, light: unlit, palette, time: many ? 0 : time, links: calm, glows: calm })
        dirty = false
        drawn = { ...cam, angle: 0 }
      }
      cam.angle = theme === 'galaxy' && !reduced ? time * 0.000015 : 0
      // The layer as drawn, moved and scaled to the camera of now, turned with the galaxy.
      const k = cam.scale / drawn.scale
      const tx = (drawn.cx - cam.cx) * cam.scale
      const ty = (drawn.cy - cam.cy) * cam.scale
      worldEl.style.transform = `rotate(${cam.angle}rad) translate(${tx}px, ${ty}px) scale(${k})`
      // Dimming the world is the compositor's job; the overlay is cleared only when it holds something.
      worldEl.style.opacity = light.focus >= 0 ? '0.35' : ''
      const blank = light.focus < 0 && light.current < 0
      if (!blank || !overlayBlank) {
        top.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0)
        drawOverlay({ ctx: top, width, height, scene, cam, light, palette })
      }
      overlayBlank = blank
      const spent = performance.now() - t0
      // What a frame costs on average, and what one that draws the world again costs.
      frameMs = frameMs ? frameMs * 0.95 + spent * 0.05 : spent
      if (redraw) drawMs = drawMs ? drawMs * 0.8 + spent * 0.2 : spent
      el.dataset.frameMs = frameMs.toFixed(2)
      el.dataset.drawMs = drawMs.toFixed(2)
      if (running || !reduced || handled) raf = requestAnimationFrame(frame)
    }
    /** Draws the overlay again on the next frame; the world too when `world` says it moved. */
    const request = (world = true) => {
      if (world) dirty = true
      if (!raf) raf = requestAnimationFrame(frame)
    }

    const fit = (c: HTMLCanvasElement, w: number, h: number) => {
      c.width = Math.round(w * devicePixelRatio)
      c.height = Math.round(h * devicePixelRatio)
      c.style.width = `${w}px`
      c.style.height = `${h}px`
    }
    const resize = () => {
      width = box.clientWidth
      height = box.clientHeight
      const diagonal = Math.ceil(Math.hypot(width, height))
      worldW = theme === 'galaxy' ? diagonal : width
      worldH = theme === 'galaxy' ? diagonal : height
      fit(el, width, height)
      fit(worldEl, worldW, worldH)
      worldEl.style.left = `${(width - worldW) / 2}px`
      worldEl.style.top = `${(height - worldH) / 2}px`
      // Twice as wide as the map: the layer drifts by half its width, then starts again.
      if (sky.current) paintStars(sky.current, width * 2, height, palette.ink, palette.mute)
      request()
    }
    const watch = new ResizeObserver(resize)
    watch.observe(box)
    resize()

    const pick = (sx: number, sy: number) => {
      const [wx, wy] = toWorld(cam, width, height, sx, sy)
      if (theme === 'city') {
        // A building is hit anywhere on its front, not only at its foot.
        let best = -1
        for (let i = 0; i < scene.x.length; i++) {
          const tall = (scene.floors?.[i] ?? 1) * 5
          if (Math.abs(wx - scene.x[i]) <= 6 && wy <= scene.y[i] + 2 && wy >= scene.y[i] - tall - 2) best = i
        }
        return best
      }
      return nearest(scene.x, scene.y, wx, wy, 14 / cam.scale)
    }
    const focus = (i: number) => {
      light.focus = i
      const links = i < 0 ? undefined : near.get(graph.notes[i].path)
      light.near = new Set([...(links?.out ?? []), ...(links?.in ?? [])].map((p) => index.get(p)).filter((x) => x !== undefined))
      request(false)
    }

    let down: { x: number; y: number; id: number } | undefined
    let dragging = false
    const local = (e: PointerEvent) => {
      const r = el.getBoundingClientRect()
      return [e.clientX - r.left, e.clientY - r.top] as const
    }
    const onDown = (e: PointerEvent) => {
      const [x, y] = local(e)
      down = { x, y, id: e.pointerId }
      dragging = false
      el.setPointerCapture(e.pointerId)
    }
    const onMove = (e: PointerEvent) => {
      const [x, y] = local(e)
      if (down && (dragging || Math.hypot(x - down.x, y - down.y) > 4)) {
        if (!dragging) setCard(undefined)
        dragging = true
        const [ax, ay] = toWorld(cam, width, height, down.x, down.y)
        const [bx, by] = toWorld(cam, width, height, x, y)
        cam.cx += ax - bx
        cam.cy += ay - by
        moved = true
        down = { ...down, x, y }
        handled = performance.now()
        request(false)
        return
      }
      if (e.pointerType !== 'mouse') return
      const i = pick(x, y)
      if (i !== light.focus) {
        focus(i)
        setCard(i < 0 ? undefined : { index: i, x, y, pinned: false })
      } else if (i >= 0) {
        setCard((c) => (c && !c.pinned ? { ...c, x, y } : c))
      }
      el.style.cursor = i >= 0 ? 'pointer' : 'grab'
    }
    const onUp = (e: PointerEvent) => {
      const was = down
      down = undefined
      if (!was || dragging) return
      const [x, y] = local(e)
      const i = pick(x, y)
      if (i >= 0 && e.pointerType === 'mouse') {
        openRef.current(graph.notes[i].path)
        return
      }
      focus(i)
      setCard(i < 0 ? undefined : { index: i, x, y, pinned: true })
    }
    const onLeave = (e: PointerEvent) => {
      if (e.pointerType !== 'mouse' || down) return
      focus(-1)
      setCard((c) => (c?.pinned ? c : undefined))
    }
    const onWheel = (e: WheelEvent) => {
      e.preventDefault()
      const r = el.getBoundingClientRect()
      const sx = e.clientX - r.left
      const sy = e.clientY - r.top
      const [wx, wy] = toWorld(cam, width, height, sx, sy)
      cam.scale = Math.max(0.15, Math.min(8, cam.scale * Math.exp(-e.deltaY * 0.0015)))
      const [nx, ny] = toWorld(cam, width, height, sx, sy)
      cam.cx += wx - nx
      cam.cy += wy - ny
      moved = true
      setCard(undefined)
      handled = performance.now()
      request(false)
    }
    el.addEventListener('pointerdown', onDown)
    el.addEventListener('pointermove', onMove)
    el.addEventListener('pointerup', onUp)
    el.addEventListener('pointerleave', onLeave)
    el.addEventListener('wheel', onWheel, { passive: false })
    return () => {
      cancelAnimationFrame(raf)
      watch.disconnect()
      el.removeEventListener('pointerdown', onDown)
      el.removeEventListener('pointermove', onMove)
      el.removeEventListener('pointerup', onUp)
      el.removeEventListener('pointerleave', onLeave)
      el.removeEventListener('wheel', onWheel)
    }
  }, [graph, theme, reduced, current, near, index])

  const note = card ? graph.notes[card.index] : undefined
  const links = note ? near.get(note.path) : undefined
  const title = (p: string) => graph.notes[index.get(p) ?? -1]?.title ?? p

  return (
    <div className="brain-canvas-wrap" ref={wrap}>
      {theme === 'galaxy' && <canvas ref={sky} className="brain-stars" aria-hidden />}
      <canvas ref={world} className="brain-world" aria-hidden />
      <canvas
        ref={canvas}
        className="brain-canvas"
        role="img"
        aria-label={`Map of ${graph.notes.length} notes and ${graph.links.length} links. Point at a note to see its links; click it to open it in the wiki.`}
      />
      {note && card && (
        <div
          className={`brain-card cut-lg${card.pinned ? ' brain-card-pinned' : ''}`}
          style={{ left: `min(${card.x + 16}px, calc(100% - 280px))`, top: `min(${card.y + 16}px, calc(100% - 220px))` }}
        >
          <p className="brain-card-title">{note.title}</p>
          <Badges kind={note.kind} status={note.status} />
          <p className="mono brain-card-path">{note.path}</p>
          <LinkList label="Links to" paths={links?.out ?? []} title={title} />
          <LinkList label="Linked from" paths={links?.in ?? []} title={title} />
          {card.pinned ? (
            <button type="button" className="btn btn-primary" onClick={() => onOpen(note.path)}>
              Open in the wiki
            </button>
          ) : (
            <p className="hint">Click to open it in the wiki.</p>
          )}
        </div>
      )}
    </div>
  )
}

function LinkList({ label, paths, title }: { label: string; paths: string[]; title: (path: string) => string }) {
  if (paths.length === 0) return null
  const shown = paths.slice(0, 6)
  return (
    <div className="brain-card-links">
      <span className="label">{label}</span>
      <ul>
        {shown.map((p) => (
          <li key={p}>{title(p)}</li>
        ))}
        {paths.length > shown.length && <li className="hint">and {paths.length - shown.length} more</li>}
      </ul>
    </div>
  )
}

function lerp(from: Camera, to: Camera, t: number): Camera {
  return {
    cx: from.cx + (to.cx - from.cx) * t,
    cy: from.cy + (to.cy - from.cy) * t,
    scale: from.scale + (to.scale - from.scale) * t,
    angle: from.angle,
  }
}
