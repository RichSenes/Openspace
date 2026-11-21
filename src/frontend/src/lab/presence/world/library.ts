import { rng } from '../body/shapes'
import { bayerAt, ditherBands, hash1, hex, LayerCache } from './pixels'
import type { Hour, Scene, Weather } from './types'

/**
 * The endless library, told with a few large shapes: two walls of shelves that run away into the
 * dark toward a vanishing point behind Shiori, an impossible room hanging upside down far above,
 * a staircase that climbs into nothing, and a handful of great paper lanterns. The middle stays
 * an empty, dark well, so Shiori is always the brightest thing. The walls are drawn once; only the
 * lanterns sway and, in some moods, a few pages drift.
 */
export interface Library {
  w: number
  h: number
  floor: number
  vx: number
  vy: number
  lanterns: { x: number; y: number; drop: number; size: number; seed: number }[]
  pages: { x: number; y: number; seed: number }[]
}

interface Light {
  void: string[]
  wood: string[]
  books: string[]
  paper: string
  lamp: number
}

export const LIBRARY_LIGHTS: Record<Hour, Light> = {
  night: { void: ['#030204', '#07040a', '#0e0810', '#160c14'], wood: ['#120604', '#1e0a06', '#2e120a'], books: ['#2a0c10', '#1e1428', '#14201a', '#2e2410'], paper: '#e8c890', lamp: 1 },
  dusk: { void: ['#060306', '#0e060a', '#180a0e', '#241014'], wood: ['#1c0806', '#2c0e08', '#42180c'], books: ['#3a1016', '#261a34', '#1a2a20', '#3a2e12'], paper: '#f0d098', lamp: 0.85 },
  dawn: { void: ['#08070c', '#100c14', '#1a141e', '#241c28'], wood: ['#22100c', '#341a12', '#4a2618'], books: ['#3e1a1e', '#2a2440', '#1e3228', '#40341a'], paper: '#f0e0c0', lamp: 0.45 },
  day: { void: ['#0e0a08', '#1a120e', '#281c14', '#36261a'], wood: ['#2a140c', '#3e2012', '#56301a'], books: ['#4e1c1e', '#33284a', '#243c2c', '#54441e'], paper: '#f8ecd0', lamp: 0.2 },
}

export function makeLibrary(w: number, h: number, seed = 91): Library {
  const r = rng(seed)
  const floor = Math.round(h * 0.9)
  const vx = Math.round(w / 2)
  const vy = Math.round(h * 0.4)
  // A few great lanterns, hanging from far above at different depths, never in front of Shiori.
  const lanterns = [0.08, 0.2, 0.8, 0.92, 0.3, 0.7].map((k, i) => ({ x: Math.round(w * k), y: 0, drop: Math.round(h * (0.18 + r() * 0.25)), size: i < 4 ? 9 : 5, seed: r() }))
  const pages = Array.from({ length: 14 }, () => ({ x: r(), y: r(), seed: r() }))
  return { w, h, floor, vx, vy, lanterns, pages }
}

/** How far into the wall a column at fraction u (0 near, 1 far) stands: closer columns spread wider. */
export const depthAt = (u: number) => 1 - 1 / (1 + u * 4)

function shade(colour: string, k: number): string {
  const [r, g, b] = hex(colour)
  const f = (v: number) => Math.round(v * k)
  return `rgb(${f(r)},${f(g)},${f(b)})`
}

/** One wall of shelves, from the near edge (x0) toward the far edge near the vanishing point. */
function wall(g: CanvasRenderingContext2D, lib: Library, L: Light, side: -1 | 1) {
  const { w, floor, vx, vy } = lib
  const near = side < 0 ? 0 : w
  const far = vx + side * w * 0.17
  const columns = 18
  // Top and bottom of the wall at an x: lines from the near corners to the vanishing point.
  const topAt = (x: number) => Math.round((-floor * 0.1) + ((vy - -floor * 0.1) * (x - near)) / (vx - near))
  const botAt = (x: number) => Math.round(floor + ((vy - floor) * (x - near)) / (vx - near))
  for (let c = 0; c < columns; c++) {
    const xa = near + (far - near) * depthAt(c / columns)
    const xb = near + (far - near) * depthAt((c + 1) / columns)
    const lo = Math.round(Math.min(xa, xb))
    const hi = Math.round(Math.max(xa, xb))
    const dim = 1 - (c / columns) * 0.75
    for (let x = lo; x < hi; x++) {
      const top = topAt(x)
      const bot = botAt(x)
      const rows = 7
      for (let rI = 0; rI < rows; rI++) {
        const y0 = Math.round(top + ((bot - top) * rI) / rows)
        const y1 = Math.round(top + ((bot - top) * (rI + 1)) / rows)
        // The shelf board, then the books standing on it.
        g.fillStyle = shade(L.wood[2], dim)
        g.fillRect(x, y1 - 1, 1, 1)
        const book = Math.floor(hash1(Math.floor((x - lo) / Math.max(1, (hi - lo) / 6)) * 31 + c * 7 + rI * 13) * 4)
        const tall = 0.55 + hash1(x * 3 + rI) * 0.35
        g.fillStyle = shade(L.wood[0], dim)
        g.fillRect(x, y0, 1, y1 - y0 - 1)
        g.fillStyle = shade(L.books[book], dim)
        const bh = Math.round((y1 - y0 - 2) * tall)
        g.fillRect(x, y1 - 1 - bh, 1, bh)
      }
      // The column's frame.
      if (x === lo) {
        g.fillStyle = shade(L.wood[1], dim)
        g.fillRect(x, top, Math.max(1, Math.round((hi - lo) * 0.12)), bot - top)
      }
    }
  }
}

/** Everything that stands still: the void, the two walls, the floor, the impossible room. */
function drawStill(g: CanvasRenderingContext2D, lib: Library, L: Light) {
  const img = g.createImageData(lib.w, lib.h)
  ditherBands(img, [...L.void].reverse().concat(L.void), 1)
  g.putImageData(img, 0, 0)
  // The floor, its boards running to the vanishing point.
  for (let y = lib.vy + 4; y < lib.h; y++) {
    const k = (y - lib.vy) / (lib.h - lib.vy)
    g.fillStyle = shade(L.wood[0], 0.4 + 0.6 * k)
    g.fillRect(0, y, lib.w, 1)
  }
  g.fillStyle = shade(L.wood[1], 0.8)
  for (let i = -8; i <= 8; i++) {
    const xb = lib.vx + i * lib.w * 0.09
    for (let y = lib.vy + 6; y < lib.h; y++) {
      const k = (y - lib.vy) / (lib.h - lib.vy)
      g.fillRect(Math.round(lib.vx + (xb - lib.vx) * k), y, 1, 1)
    }
  }
  wall(g, lib, L, -1)
  wall(g, lib, L, 1)
  // Far above, a room hanging upside down: its floor is our ceiling. Dim, a silhouette.
  const rx = Math.round(lib.vx - lib.w * 0.1)
  const ry = Math.round(lib.h * 0.04)
  const rw = Math.round(lib.w * 0.2)
  g.fillStyle = shade(L.wood[1], 0.55)
  g.fillRect(rx, ry, rw, 3)
  g.fillStyle = shade(L.wood[0], 0.6)
  for (let x = rx + 2; x < rx + rw - 2; x += 5) g.fillRect(x, ry + 3, 3, 8 + ((x >> 2) % 3))
  // A staircase that climbs out of the left wall and stops in the air.
  g.fillStyle = shade(L.wood[2], 0.6)
  for (let i = 0; i < 9; i++) g.fillRect(Math.round(lib.w * 0.27) + i * 3, Math.round(lib.h * 0.36) - i * 3, 4, 1)
  // The dark well behind Shiori.
  const rxw = lib.w * 0.2
  const ryw = lib.h * 0.42
  g.fillStyle = L.void[0]
  for (let y = Math.round(lib.vy - ryw); y < lib.vy + ryw; y++)
    for (let x = Math.round(lib.vx - rxw); x < lib.vx + rxw; x++) {
      const d = Math.hypot((x - lib.vx) / rxw, (y - lib.vy) / ryw)
      if (d < 1 && y >= 0 && bayerAt(x, y) < (1 - d) ** 0.5 * 0.95) g.fillRect(x, y, 1, 1)
    }
}

const still = new LayerCache()

/** One frame: the still layer, a few swaying lanterns, and the mood. */
export function drawLibrary(g: CanvasRenderingContext2D, lib: Library, time: number, calm: boolean, weather: Weather) {
  const t = calm ? 0 : time
  const L = LIBRARY_LIGHTS[weather.hour]
  const sky = weather.sky
  g.drawImage(still.get('library', weather.hour, lib.w, lib.h, (c) => drawStill(c, lib, L)), 0, 0)

  // In a shift the hanging room slowly turns over, and back.
  if (sky === 'Storm') {
    const k = (Math.sin(t * 0.3) + 1) / 2
    const rx = Math.round(lib.vx - lib.w * 0.1)
    const rw = Math.round(lib.w * 0.2)
    g.fillStyle = shade(L.wood[1], 0.7)
    const y = Math.round(lib.h * 0.04 + k * lib.h * 0.06)
    g.fillRect(rx + Math.round(k * rw * 0.2), y, Math.round(rw * (1 - k * 0.4)), 2)
  }

  // The great lanterns: they sway slowly on long cords and glow.
  for (const l of lib.lanterns) {
    const sway = calm ? 0 : Math.sin(t * 0.5 + l.seed * 6) * 2
    const x = Math.round(l.x + sway)
    const y = l.drop
    g.fillStyle = '#0a0404'
    for (let k = 0; k < y; k += 1) g.fillRect(Math.round(l.x + (sway * k) / y), k, 1, 1)
    if (L.lamp > 0.05) {
      const glow = (calm ? 1 : 0.9 + 0.1 * Math.sin(t * 2 + l.seed * 9)) * L.lamp
      g.fillStyle = `rgba(255, 140, 60, ${0.14 * glow})`
      g.fillRect(x - l.size, y - 2, l.size * 2 + 1, l.size * 2 + 4)
    }
    g.fillStyle = '#3a0a08'
    g.fillRect(x - Math.round(l.size / 2), y, l.size, 1)
    g.fillRect(x - Math.round(l.size / 2), y + Math.round(l.size * 1.3), l.size, 1)
    g.fillStyle = L.lamp > 0.3 ? '#ff9a48' : shade(L.paper, 0.8)
    g.fillRect(x - Math.round(l.size / 2) + 1, y + 1, l.size - 2, Math.round(l.size * 1.3) - 1)
    g.fillStyle = L.lamp > 0.3 ? '#ffd890' : L.paper
    g.fillRect(x - 1, y + 2, 2, Math.round(l.size * 1.3) - 3)
  }

  // Moods: only a few things in the air, never in front of Shiori.
  const drifting = sky === 'Rain' ? 14 : sky === 'Drizzle' ? 5 : sky === 'Cloudy' || sky === 'Snow' ? 10 : 0
  for (let i = 0; i < drifting; i++) {
    const p = lib.pages[i % lib.pages.length]
    const fall = sky === 'Snow' ? -4 : 6
    const y = Math.round((((p.y * lib.h + t * fall * (0.6 + p.seed)) % lib.h) + lib.h) % lib.h)
    let x = Math.round((p.x * lib.w + Math.sin(t * 0.5 + p.seed * 9) * 10) % lib.w)
    if (Math.abs(x - lib.vx) < lib.w * 0.2) x = x < lib.vx ? x - lib.w * 0.2 : x + lib.w * 0.2
    g.fillStyle = sky === 'Snow' ? '#8a7a70' : sky === 'Cloudy' ? '#6a5040' : L.paper
    const flip = Math.sin(t * 2 + p.seed * 20) > 0
    if (sky === 'Rain' || sky === 'Drizzle') g.fillRect(x, y, flip ? 4 : 2, flip ? 2 : 3)
    else g.fillRect(x, y, 1, 1)
  }
  if (sky === 'Fog') {
    g.fillStyle = 'rgba(120, 90, 70, 0.12)'
    g.fillRect(0, 0, lib.w, lib.h)
  }
}

export function libraryScene(w: number, h: number): Scene {
  const lib = makeLibrary(w, h)
  return { w, h, draw: (g, time, calm, weather) => drawLibrary(g, lib, time, calm, weather) }
}
