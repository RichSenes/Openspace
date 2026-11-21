import { rng } from '../body/shapes'
import { bayerAt, ditherBands, hash1 } from './pixels'
import type { Hour, Scene, Weather } from './types'

/**
 * The endless library: a hall that goes on in every direction, floors of bookshelves stacked
 * above and below, some upside down, bridges and stairs crossing the void, paper lanterns, sliding
 * doors that open and close by themselves. Shiori's name is a bookmark: this is where she lives.
 *
 * Hours are the library's light (lamps lit, morning through the paper screens, the great lamp,
 * candles only). Skies are its moods: still, dust in the air, haze, pages drifting, pages falling,
 * ash, and a shift, when the rooms rearrange themselves.
 */
export interface Tier {
  /** Top of the tier, in art pixels; tiers are stacked and wrap around the frame. */
  y: number
  h: number
  /** Upside down: books hang from the ceiling. */
  flipped: boolean
  /** Horizontal scroll speed (parallax), and an offset. */
  depth: number
  offset: number
  books: { x: number; w: number; h: number; c: number }[]
  doors: { x: number; w: number; seed: number }[]
  lanterns: { x: number; drop: number }[]
}

export interface Library {
  w: number
  h: number
  floor: number
  tiers: Tier[]
  bridges: { x: number; y: number; w: number; depth: number }[]
  stairs: { x: number; y: number; n: number; dir: 1 | -1 }[]
  /** Width of one repeat of a tier's pattern. */
  period: number
}

interface Light {
  void: string[]
  wood: string[]
  paper: string
  lamp: number
  books: string[]
}

export const LIBRARY_LIGHTS: Record<Hour, Light> = {
  night: { void: ['#040205', '#0a0408', '#140810', '#1e0c14'], wood: ['#1a0806', '#2e0e0a', '#4a1810'], paper: '#e8c890', lamp: 1, books: ['#3a0e14', '#2a1a3a', '#1a2a24', '#4a3a14', '#3a1a0c'] },
  dusk: { void: ['#0a0408', '#1a080c', '#2e1014', '#40181a'], wood: ['#2a0c08', '#48160e', '#6e2414'], paper: '#f0d098', lamp: 0.85, books: ['#5a1420', '#3a2650', '#24402e', '#6a5418', '#5a2810'] },
  dawn: { void: ['#0c0a12', '#1a1420', '#2a2030', '#3a2c3c'], wood: ['#2e140e', '#4a2216', '#6a3420'], paper: '#f0e0c0', lamp: 0.4, books: ['#5a2028', '#3a3058', '#2e4a3a', '#6a5a2a', '#5a3218'] },
  day: { void: ['#140c0a', '#281812', '#3e261a', '#563622'], wood: ['#3a1a10', '#5a2c18', '#7e4224'], paper: '#f8ecd0', lamp: 0.15, books: ['#7a2a2e', '#4a3c6a', '#3a5a44', '#8a7234', '#6e3a1c'] },
}

function makeTier(r: () => number, y: number, h: number, flipped: boolean, depth: number, period: number): Tier {
  const books: Tier['books'] = []
  const doors: Tier['doors'] = []
  const lanterns: Tier['lanterns'] = []
  let x = 0
  while (x < period) {
    if (r() < 0.12) {
      const w = 10 + Math.floor(r() * 6)
      doors.push({ x, w, seed: r() })
      x += w + 2
      continue
    }
    const w = 1 + Math.floor(r() * 3)
    books.push({ x, w, h: Math.max(3, Math.round(h * (0.5 + r() * 0.35))), c: Math.floor(r() * 5) })
    x += w
    if (r() < 0.04) x += 2
  }
  for (let lx = Math.floor(r() * 30); lx < period; lx += 30 + Math.floor(r() * 30)) lanterns.push({ x: lx, drop: 3 + Math.floor(r() * 6) })
  return { y, h, flipped, depth, offset: Math.floor(r() * period), books, doors, lanterns }
}

export function makeLibrary(w: number, h: number, seed = 91): Library {
  const r = rng(seed)
  const floor = Math.round(h * 0.9)
  const period = Math.max(160, w)
  const tiers: Tier[] = []
  // Far tiers small and slow, near ones taller; some hang upside down.
  let y = -4
  let k = 0
  while (y < floor) {
    const near = k % 3 === 2
    const th = near ? 26 + Math.floor(r() * 8) : 14 + Math.floor(r() * 6)
    tiers.push(makeTier(r, y, th, r() < 0.3, near ? 1 : 0.35 + r() * 0.3, period))
    y += th + 8 + Math.floor(r() * 10)
    k++
  }
  const bridges = Array.from({ length: 4 }, () => ({ x: Math.floor(r() * w), y: Math.floor(r() * floor * 0.9), w: 30 + Math.floor(r() * 50), depth: 0.5 + r() * 0.5 }))
  const stairs = Array.from({ length: 3 }, () => ({ x: Math.floor(r() * w), y: Math.floor(r() * floor * 0.8), n: 6 + Math.floor(r() * 8), dir: (r() < 0.5 ? 1 : -1) as 1 | -1 }))
  return { w, h, floor, tiers, bridges, stairs, period }
}

/** Where a tier stands at a time: tiers slide sideways, and in a shift they also slide up or down. */
export function tierShift(tier: Tier, i: number, t: number, shifting: boolean): { dx: number; dy: number } {
  const dx = t * 2 * tier.depth + tier.offset
  if (!shifting) return { dx, dy: 0 }
  // Every few seconds a tier glides to a new height and stays there a while.
  const cycle = 6
  const n = Math.floor((t + i * 1.7) / cycle)
  const p = ((t + i * 1.7) % cycle) / cycle
  const from = (hash1(n * 7 + i) - 0.5) * 30
  const to = (hash1((n + 1) * 7 + i) - 0.5) * 30
  const e = p < 0.75 ? 0 : (1 - Math.cos(((p - 0.75) / 0.25) * Math.PI)) / 2
  return { dx, dy: Math.round(from + (to - from) * e) }
}

function drawTier(g: CanvasRenderingContext2D, lib: Library, tier: Tier, i: number, L: Light, t: number, calm: boolean, shifting: boolean) {
  const { dx, dy } = tierShift(tier, i, calm ? 0 : t, shifting && !calm)
  const top = tier.y + dy
  const far = tier.depth < 0.9
  const wood = far ? L.wood[0] : L.wood[1]
  const beam = far ? L.wood[1] : L.wood[2]
  // The floor (or, flipped, the ceiling) and the back wall.
  g.fillStyle = L.void[far ? 1 : 2]
  g.fillRect(0, top, lib.w, tier.h)
  g.fillStyle = beam
  const plank = tier.flipped ? top : top + tier.h - 2
  g.fillRect(0, plank, lib.w, 2)
  g.fillStyle = wood
  g.fillRect(0, tier.flipped ? top + tier.h - 1 : top, lib.w, 1)
  for (let px = -((dx % 12) + 12) % 12; px < lib.w; px += 12) g.fillRect(Math.round(px), top, 1, tier.h)

  const shift = Math.round(dx) % lib.period
  const each = (x: number) => {
    let v = (x - shift) % lib.period
    if (v < 0) v += lib.period
    return v
  }
  for (const b of tier.books) {
    for (let rep = 0; rep < 2; rep++) {
      const x = each(b.x) + rep * lib.period - lib.period * (rep ? 1 : 0)
      if (x > lib.w || x + b.w < 0) continue
      g.fillStyle = far ? L.void[3] : L.books[b.c]
      const by = tier.flipped ? top + 2 : plank - b.h
      g.fillRect(x, by, b.w, b.h)
      if (!far && b.w > 1) {
        g.fillStyle = '#c8a050'
        g.fillRect(x, by + Math.round(b.h * 0.3), b.w, 1)
      }
    }
  }
  for (const d of tier.doors) {
    const x = each(d.x)
    if (x > lib.w) continue
    // Sliding doors: paper panels in a wooden grid that slide open and closed.
    const open = calm ? 0 : Math.max(0, Math.sin(t * (0.4 + d.seed * 0.5) + d.seed * 20)) * (d.w / 2 - 1)
    const dh = tier.h - 4
    const dyy = tier.flipped ? top + 2 : top + 2
    g.fillStyle = '#050203'
    g.fillRect(x, dyy, d.w, dh)
    g.fillStyle = L.paper
    g.globalAlpha = far ? 0.35 : 0.55 + 0.3 * L.lamp
    g.fillRect(x - Math.round(open), dyy, Math.ceil(d.w / 2), dh)
    g.fillRect(x + Math.floor(d.w / 2) + Math.round(open), dyy, Math.ceil(d.w / 2), dh)
    g.globalAlpha = 1
    g.fillStyle = wood
    for (const half of [x - Math.round(open), x + Math.floor(d.w / 2) + Math.round(open)]) {
      g.fillRect(half, dyy, Math.ceil(d.w / 2), 1)
      g.fillRect(half, dyy + dh - 1, Math.ceil(d.w / 2), 1)
      for (let gx = 0; gx < d.w / 2; gx += 3) g.fillRect(half + gx, dyy, 1, dh)
      for (let gy = 0; gy < dh; gy += 4) g.fillRect(half, dyy + gy, Math.ceil(d.w / 2), 1)
    }
  }
  if (L.lamp > 0.05)
    for (const l of tier.lanterns) {
      const x = each(l.x)
      if (x > lib.w) continue
      const swing = calm ? 0 : Math.round(Math.sin(t * 1.1 + l.x) * 0.6)
      const ly = tier.flipped ? top + tier.h + l.drop : top + 1 + l.drop
      g.fillStyle = '#1a0a06'
      g.fillRect(x, tier.flipped ? top + tier.h : top, 1, l.drop + 1)
      const flick = calm ? 1 : 0.85 + 0.15 * Math.sin(t * 8 + l.x)
      g.fillStyle = `rgba(255, 150, 70, ${0.16 * L.lamp * flick})`
      for (let r = 6; r > 2; r--) g.fillRect(x + swing - r, ly - r + 2, r * 2 + 1, r * 2 + 1)
      g.fillStyle = '#ff9a40'
      g.fillRect(x + swing - 1, ly, 3, 4)
      g.fillStyle = '#ffd890'
      g.fillRect(x + swing, ly + 1, 1, 2)
      g.fillStyle = '#5a1010'
      g.fillRect(x + swing - 1, ly - 1, 3, 1)
      g.fillRect(x + swing - 1, ly + 4, 3, 1)
    }
}

/** One frame of the library over its void, drawn once per hour by the scene. */
export function drawLibrary(g: CanvasRenderingContext2D, lib: Library, back: HTMLCanvasElement, time: number, calm: boolean, weather: Weather) {
  const t = calm ? 0 : time
  const L = LIBRARY_LIGHTS[weather.hour]
  const sky = weather.sky
  const shifting = sky === 'Storm'
  g.drawImage(back, 0, 0)

  lib.tiers.forEach((tier, i) => {
    if (tier.depth < 0.9) drawTier(g, lib, tier, i, L, t, calm, shifting)
  })
  // Bridges and stairs across the void, between the far and the near tiers.
  for (const b of lib.bridges) {
    const x = Math.round((((b.x - t * 3 * b.depth) % (lib.w + b.w)) + lib.w + b.w) % (lib.w + b.w)) - b.w
    g.fillStyle = L.wood[1]
    g.fillRect(x, b.y, b.w, 2)
    g.fillStyle = L.wood[2]
    for (let k = 0; k < b.w; k += 4) g.fillRect(x + k, b.y - 4, 1, 4)
    g.fillRect(x, b.y - 4, b.w, 1)
  }
  for (const st of lib.stairs) {
    g.fillStyle = L.wood[1]
    for (let k = 0; k < st.n; k++) g.fillRect(st.x + k * 3 * st.dir, st.y + k * 2, 3, 1)
  }
  lib.tiers.forEach((tier, i) => {
    if (tier.depth >= 0.9) drawTier(g, lib, tier, i, L, t, calm, shifting)
  })

  // Behind Shiori the hall opens into a dark well, so she always reads first.
  const cx = lib.w / 2
  const cy = lib.floor * 0.42
  const rx = lib.w * 0.19
  const ry = lib.floor * 0.44
  g.fillStyle = L.void[0]
  for (let y = Math.max(0, Math.round(cy - ry)); y < Math.min(lib.floor, cy + ry); y++)
    for (let x = Math.max(0, Math.round(cx - rx)); x < Math.min(lib.w, cx + rx); x++) {
      const d = Math.hypot((x - cx) / rx, (y - cy) / ry)
      if (d < 1 && bayerAt(x, y) < (1 - d) ** 0.6 * 0.92) g.fillRect(x, y, 1, 1)
    }

  // Moods: what drifts in the air.
  const drift = (n: number, colour: string, speed: number, size: [number, number], up: boolean) => {
    g.fillStyle = colour
    for (let i = 0; i < n; i++) {
      const s = hash1(i)
      const y0 = hash1(i + 30) * lib.h
      const y = Math.round(((((up ? -1 : 1) * t * speed * (0.5 + s) + y0) % lib.h) + lib.h) % lib.h)
      const x = Math.round((s * lib.w + Math.sin(t * 0.6 + i) * 6) % lib.w)
      g.fillRect(x, y, Math.sin(t * 2 + i) > 0 ? size[0] : size[1], 1)
    }
  }
  if (sky === 'Cloudy') drift(Math.round(lib.w * 0.25), '#8a6a50', 3, [1, 1], false)
  if (sky === 'Fog') {
    g.fillStyle = L.void[3]
    for (let y = 0; y < lib.floor; y++)
      for (let x = 0; x < lib.w; x++) if (bayerAt(x + Math.round(t * 2), y) < 0.22 + 0.12 * Math.sin(x * 0.03 + y * 0.05 + t * 0.2)) g.fillRect(x, y, 1, 1)
  }
  if (sky === 'Drizzle') drift(10, L.paper, 10, [3, 2], false)
  if (sky === 'Rain') drift(36, L.paper, 18, [3, 2], false)
  if (sky === 'Snow') drift(Math.round(lib.w * 0.2), '#a89890', 6, [1, 1], true)
  if (sky === 'Clear') drift(Math.round(lib.w * 0.06), '#e0b060', 2, [1, 1], true)

  // The floor we stand on: polished boards that reflect the lanterns.
  g.fillStyle = L.wood[0]
  g.fillRect(0, lib.floor, lib.w, lib.h - lib.floor)
  g.fillStyle = L.wood[1]
  g.fillRect(0, lib.floor, lib.w, 1)
  g.fillStyle = '#050203'
  for (let x = 0; x < lib.w; x += 10) g.fillRect(x, lib.floor + 1, 1, lib.h - lib.floor)
  if (L.lamp > 0.3) {
    g.fillStyle = `rgba(255, 150, 70, ${0.25 * L.lamp})`
    for (let x = 6; x < lib.w; x += 40) g.fillRect(x, lib.floor + 3, 2, lib.h - lib.floor - 4)
  }
  // A shift: the hall lurches, a flash of the lanterns.
  if (shifting && !calm && (t % 6) > 4.5 && (t % 6) < 4.6) {
    g.fillStyle = 'rgba(255, 140, 60, 0.12)'
    g.fillRect(0, 0, lib.w, lib.h)
  }
}

export function libraryScene(w: number, h: number): Scene {
  const lib = makeLibrary(w, h)
  let back: HTMLCanvasElement | null = null
  let key = ''
  return {
    w,
    h,
    draw(g, time, calm, weather) {
      if (key !== weather.hour) {
        back = document.createElement('canvas')
        back.width = w
        back.height = h
        const bg = back.getContext('2d')!
        const img = bg.createImageData(w, h)
        ditherBands(img, LIBRARY_LIGHTS[weather.hour].void, 0.9)
        bg.putImageData(img, 0, 0)
        key = weather.hour
      }
      drawLibrary(g, lib, back!, time, calm, weather)
    },
  }
}
