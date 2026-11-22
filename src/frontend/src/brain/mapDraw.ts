import { hash, LOT, type CityBlock } from './graph'

/**
 * Drawing the map on a 2D canvas. Everything costly (the glow of a point, the star field) is
 * drawn once into small canvases and stamped; a frame is a few hundred stamps and two paths of
 * lines, well under the 4 ms budget for 500 notes.
 */

export type MapTheme = 'galaxy' | 'city'

export interface Palette {
  bg: string
  panel: string
  raised: string
  line: string
  edge: string
  ink: string
  mute: string
  accent: string
  lit: string
  kinds: Record<string, string>
  other: string
}

/** What the canvas shows: the notes' places, their links as index pairs, their colours. */
export interface Scene {
  theme: MapTheme
  titles: string[]
  x: Float64Array
  y: Float64Array
  source: Int32Array
  target: Int32Array
  colors: string[]
  /** City only. */
  floors?: Uint8Array
  blocks?: CityBlock[]
  /** City only: the buildings standing on the same line, from the back row to the front. */
  rows?: number[][]
  seeds: number[]
}

export interface Camera {
  /** The world point at the middle of the canvas. */
  cx: number
  cy: number
  scale: number
  /** The galaxy's slow turn, in radians. */
  angle: number
}

/** What is lit: the note under the pointer and its neighbours; -1 for none. */
export interface Highlight {
  focus: number
  near: Set<number>
  current: number
}

export function toScreen(cam: Camera, w: number, h: number, x: number, y: number): [number, number] {
  const dx = x - cam.cx
  const dy = y - cam.cy
  const c = Math.cos(cam.angle)
  const s = Math.sin(cam.angle)
  return [(dx * c - dy * s) * cam.scale + w / 2, (dx * s + dy * c) * cam.scale + h / 2]
}

export function toWorld(cam: Camera, w: number, h: number, sx: number, sy: number): [number, number] {
  const rx = (sx - w / 2) / cam.scale
  const ry = (sy - h / 2) / cam.scale
  const c = Math.cos(-cam.angle)
  const s = Math.sin(-cam.angle)
  return [rx * c - ry * s + cam.cx, rx * s + ry * c + cam.cy]
}

const glows = new Map<string, HTMLCanvasElement>()

/** A soft round light of a colour at a size in pixels, drawn once. */
function glow(color: string, size: number): HTMLCanvasElement {
  const key = `${color}/${size}`
  let sprite = glows.get(key)
  if (!sprite) {
    sprite = document.createElement('canvas')
    sprite.width = sprite.height = size
    const g = sprite.getContext('2d')!
    const r = size / 2
    const gradient = g.createRadialGradient(r, r, 0, r, r, r)
    gradient.addColorStop(0, color)
    gradient.addColorStop(0.25, color + '88')
    gradient.addColorStop(1, color + '00')
    g.fillStyle = gradient
    g.fillRect(0, 0, size, size)
    glows.set(key, sprite)
  }
  return sprite
}

/** Square pixel stars in two sizes, placed from a hash so they never jump between renders. */
export function paintStars(stars: HTMLCanvasElement, width: number, height: number, ink: string, mute: string) {
  stars.width = Math.max(1, Math.ceil(width))
  stars.height = Math.max(1, Math.ceil(height))
  const g = stars.getContext('2d')!
  const count = Math.round((width * height) / 2600)
  for (let i = 0; i < count; i++) {
    const h = hash(`star${i}`)
    const x = h % stars.width
    const y = (h >>> 11) % stars.height
    const size = (h >>> 24) % 9 === 0 ? 2 : 1
    g.globalAlpha = 0.25 + ((h >>> 4) % 6) / 10
    g.fillStyle = (h >>> 20) % 4 === 0 ? ink : mute
    g.fillRect(x, y, size, size)
  }
}

export interface FrameInput {
  ctx: CanvasRenderingContext2D
  width: number
  height: number
  scene: Scene
  cam: Camera
  light: Highlight
  palette: Palette
  /** Milliseconds since the map opened; 0 with reduced motion. */
  time: number
  /** False while the galaxy settles with many notes: long lines are the costly part of a frame. */
  links: boolean
  /** Draws only these notes and the links of the one in focus: the highlight on the overlay. */
  only?: Set<number>
  /** False while settling with many notes: points without their glow. */
  glows?: boolean
}

export function drawFrame(input: FrameInput) {
  const { ctx, width, height } = input
  ctx.imageSmoothingEnabled = false
  // The ground and the stars are on the layer underneath, painted once.
  ctx.clearRect(0, 0, width, height)
  if (input.scene.theme === 'galaxy') drawGalaxy(input)
  else drawCity(input)
}

function drawGalaxy({ ctx, width, height, scene, cam, light, palette, links, only, glows = true }: FrameInput) {
  const n = scene.x.length
  const sx = new Float32Array(n)
  const sy = new Float32Array(n)
  for (let i = 0; i < n; i++) {
    if (only && !only.has(i)) continue
    ;[sx[i], sy[i]] = toScreen(cam, width, height, scene.x[i], scene.y[i])
  }
  const lit = light.focus >= 0

  ctx.lineWidth = 1
  ctx.strokeStyle = palette.accent
  ctx.globalAlpha = lit ? 0.06 : 0.14
  ctx.beginPath()
  for (let k = 0; links && !only && k < scene.source.length; k++) {
    const a = scene.source[k]
    const b = scene.target[k]
    ctx.moveTo(sx[a], sy[a])
    ctx.lineTo(sx[b], sy[b])
  }
  ctx.stroke()
  if (lit) {
    ctx.globalAlpha = 0.85
    ctx.beginPath()
    for (let k = 0; k < scene.source.length; k++) {
      const a = scene.source[k]
      const b = scene.target[k]
      if (a !== light.focus && b !== light.focus) continue
      ctx.moveTo(sx[a], sy[a])
      ctx.lineTo(sx[b], sy[b])
    }
    ctx.stroke()
  }

  // Stamps at whole pixels and a whole size, so the canvas copies instead of resampling;
  // the light points first, then their cores in one path, the dimmed ones apart.
  const size = Math.round(Math.max(16, Math.min(34, 24 * Math.sqrt(cam.scale))))
  const big = Math.round(size * 1.5)
  for (const near of lit ? [false, true] : [true]) {
    ctx.globalAlpha = near ? 1 : 0.25
    for (let i = 0; glows && i < n; i++) {
      if (only && !only.has(i)) continue
      if (lit && near !== (i === light.focus || light.near.has(i))) continue
      const s = i === light.focus || i === light.current ? big : size
      ctx.drawImage(glow(scene.colors[i], s), Math.round(sx[i] - s / 2), Math.round(sy[i] - s / 2))
    }
    ctx.fillStyle = near ? palette.ink : palette.mute
    ctx.beginPath()
    for (let i = 0; i < n; i++) {
      if (only && !only.has(i)) continue
      if (lit && near !== (i === light.focus || light.near.has(i))) continue
      ctx.rect(Math.round(sx[i]) - 1, Math.round(sy[i]) - 1, 3, 3)
    }
    ctx.fill()
  }
  ctx.globalAlpha = 1
}

function drawCity({ ctx, width, height, scene, cam, light, palette, time, only }: FrameInput) {
  const scale = cam.scale
  ctx.lineWidth = 1
  for (const block of only ? [] : (scene.blocks ?? [])) {
    const [x, y] = toScreen(cam, width, height, block.x - 4, block.y)
    ctx.fillStyle = palette.panel
    ctx.fillRect(Math.round(x), Math.round(y), Math.round((block.width + 8) * scale), Math.round((block.height + 6) * scale))
    ctx.strokeStyle = palette.line
    ctx.strokeRect(Math.round(x) + 0.5, Math.round(y) + 0.5, Math.round((block.width + 8) * scale), Math.round((block.height + 6) * scale))
  }

  const lit = light.focus >= 0
  const floor = 5 * scale
  const wide = Math.max(3, (LOT - 4) * scale)
  // Windows change now and then, a few at a time: someone is still at work.
  const slot = time ? Math.floor(time / 4000) : 0

  // Links run between the roofs, faint unless their note is under the pointer.
  ctx.strokeStyle = palette.accent
  ctx.globalAlpha = lit ? 0.05 : 0.1
  ctx.beginPath()
  const roof = (i: number) => {
    const [x, y] = toScreen(cam, width, height, scene.x[i], scene.y[i])
    return [x, y - (scene.floors?.[i] ?? 1) * floor] as const
  }
  for (let k = 0; !only && k < scene.source.length; k++) {
    const a = roof(scene.source[k])
    const b = roof(scene.target[k])
    ctx.moveTo(a[0], a[1])
    ctx.lineTo(b[0], b[1])
  }
  ctx.stroke()

  // Row by row from the back, so front buildings hide the ones behind; inside a row nothing
  // overlaps, so each row is three fills: walls, then roofs by colour, then lit windows.
  const pane = Math.max(1, Math.round(scale * 1.6))
  const w = Math.round(wide)
  const rows = scene.rows ?? []
  for (const all of rows) {
    const row = only ? all.filter((i) => only.has(i)) : all
    for (const near of lit ? [false, true] : [true]) {
      const members = lit ? row.filter((i) => near === (i === light.focus || light.near.has(i))) : row
      if (members.length === 0) continue
      ctx.globalAlpha = near ? 1 : 0.35
      const left = members.map((i) => Math.round(toScreen(cam, width, height, scene.x[i], scene.y[i])[0] - wide / 2))
      const top = members.map((i) => Math.round(toScreen(cam, width, height, scene.x[i], scene.y[i])[1] - (scene.floors?.[i] ?? 1) * floor))
      for (const picked of [false, true]) {
        ctx.fillStyle = picked ? palette.edge : palette.raised
        ctx.beginPath()
        members.forEach((i, k) => {
          if ((i === light.focus || i === light.current) !== picked) return
          ctx.rect(left[k], top[k], w, Math.round((scene.floors?.[i] ?? 1) * floor))
        })
        ctx.fill()
      }
      // The roof in the note's colour, like a sign.
      for (const color of new Set(members.map((i) => scene.colors[i]))) {
        ctx.fillStyle = color
        ctx.beginPath()
        members.forEach((i, k) => {
          if (scene.colors[i] === color) ctx.rect(left[k], top[k], w, Math.max(1, Math.round(scale)))
        })
        ctx.fill()
      }
      // Two windows per floor; which are lit comes from the note's seed.
      ctx.fillStyle = palette.lit
      ctx.beginPath()
      members.forEach((i, k) => {
        const floors = scene.floors?.[i] ?? 1
        for (let f = 0; f < floors; f++) {
          for (let c = 0; c < 2; c++) {
            const bit = (scene.seeds[i] >>> ((f * 2 + c) % 28)) & 1
            const flicker = slot && ((Math.imul(scene.seeds[i] ^ slot, 2654435761) >>> (f * 2 + c)) & 31) === 0 ? 1 : 0
            if ((bit ^ flicker) === 0) continue
            ctx.rect(left[k] + Math.round(wide * (c === 0 ? 0.22 : 0.62)), top[k] + Math.round(f * floor + floor * 0.4), pane, pane)
          }
        }
      })
      ctx.fill()
    }
  }
  ctx.globalAlpha = 1

  if (lit) {
    ctx.strokeStyle = palette.accent
    ctx.globalAlpha = 0.9
    ctx.beginPath()
    for (let k = 0; k < scene.source.length; k++) {
      if (scene.source[k] !== light.focus && scene.target[k] !== light.focus) continue
      const a = roof(scene.source[k])
      const b = roof(scene.target[k])
      ctx.moveTo(a[0], a[1])
      ctx.lineTo(b[0], b[1])
    }
    ctx.stroke()
    ctx.globalAlpha = 1
  }

  // Folder names on the street in front of each block, when there is room to read them.
  if (scale >= 0.8 && !only) {
    ctx.font = '11px "IBM Plex Mono", monospace'
    ctx.fillStyle = palette.mute
    ctx.textBaseline = 'top'
    for (const block of scene.blocks ?? []) {
      const [x, y] = toScreen(cam, width, height, block.x, block.y + block.height + 8)
      // A name wider than its block is cut, so it never runs into the next one.
      const room = (block.width + 8) * scale
      let name = (block.folder || 'vault').toUpperCase()
      while (name.length > 1 && ctx.measureText(name).width > room) name = name.slice(0, -2) + '…'
      ctx.fillText(name, Math.round(x - 4 * scale), Math.round(y))
    }
  }
}

/**
 * The overlay, redrawn every frame because it stays upright while the world turns under it:
 * when a note is in focus (the caller dims the world layer), the note, its neighbours and its
 * links are drawn again on top; then the titles.
 */
export function drawOverlay(input: Omit<FrameInput, 'time' | 'links'>) {
  const { ctx, width, height, light } = input
  ctx.clearRect(0, 0, width, height)
  if (light.focus >= 0) {
    const only = new Set(light.near)
    only.add(light.focus)
    const frame = { ...input, time: 0, links: true, only }
    if (input.scene.theme === 'galaxy') drawGalaxy(frame)
    else drawCity(frame)
  }
  drawLabels(input)
}

/** The titles of what is lit, on a dark plate so they read over anything. */
function drawLabels({ ctx, width, height, scene, cam, light, palette }: Omit<FrameInput, 'time' | 'links'>) {
  const show = new Set<number>(light.near)
  if (light.focus >= 0) show.add(light.focus)
  if (light.current >= 0) show.add(light.current)
  if (show.size === 0) return
  ctx.font = '12px "IBM Plex Sans", system-ui, sans-serif'
  ctx.textBaseline = 'middle'
  for (const i of show) {
    const [x, y0] = toScreen(cam, width, height, scene.x[i], scene.y[i])
    const y = scene.theme === 'city' ? y0 - (scene.floors?.[i] ?? 1) * 5 * cam.scale - 10 : y0 - 14
    const text = scene.titles[i].length > 40 ? scene.titles[i].slice(0, 39) + '…' : scene.titles[i]
    const w = ctx.measureText(text).width + 10
    const left = Math.round(Math.min(Math.max(4, x - w / 2), width - w - 4))
    ctx.globalAlpha = 0.9
    ctx.fillStyle = palette.bg
    ctx.fillRect(left, Math.round(y - 9), Math.round(w), 18)
    ctx.globalAlpha = 1
    ctx.fillStyle = i === light.focus ? palette.ink : palette.mute
    ctx.fillText(text, left + 5, Math.round(y))
  }
}
