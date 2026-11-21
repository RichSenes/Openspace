import { rng } from '../body/shapes'
import { bayerAt, ditherBands, hash1, LayerCache } from './pixels'
import type { Hour, Scene, Weather } from './types'

/**
 * The endless library, built like the city: towers of bookshelves in three depths, some rising
 * from the dark below, some hanging from the dark above as in an impossible castle, joined by
 * walkways; reading lamps lit in their windows; paper lanterns on long cords. The middle is a
 * dark well, so Shiori stands alone in front of it.
 *
 * What moves has a reason: now and then a tower glides to a new height (the library
 * rearranging itself), lanterns sway and flicker, a sliding door opens and lets light out,
 * paper cranes fly between the towers, and in some moods pages drift.
 */
export interface Tower {
  x: number
  w: number
  /** Length from its root (the floor for rising towers, the ceiling for hanging ones). */
  len: number
  hanging: boolean
  layer: 0 | 1 | 2
  seed: number
}

export interface Library {
  w: number
  h: number
  towers: Tower[]
  walkways: { x0: number; x1: number; y: number; layer: number }[]
  lanterns: { x: number; drop: number; seed: number }[]
  cranes: { seed: number; y: number; speed: number }[]
  pages: { x: number; y: number; seed: number }[]
}

interface Light {
  void: string[]
  wood: [string, string, string]
  edge: string
  lamp: string
  books: string[]
  glow: number
}

export const LIBRARY_LIGHTS: Record<Hour, Light> = {
  night: {
    void: ['#040206', '#08040a', '#0e0710', '#160a14', '#1e0e16'],
    wood: ['#1c0e10', '#2a1214', '#3a1a18'],
    edge: '#5a2a1c',
    lamp: '#ffae58',
    books: ['#4a1a20', '#2a2244', '#1e3426', '#4a3a18'],
    glow: 1,
  },
  dusk: {
    void: ['#06030a', '#0e0610', '#1a0a16', '#2a1018', '#3a1618'],
    wood: ['#24100e', '#361812', '#4a2216'],
    edge: '#7a3a20',
    lamp: '#ffb860',
    books: ['#5a2026', '#342a54', '#264030', '#5a481e'],
    glow: 0.85,
  },
  dawn: {
    void: ['#08070e', '#100c16', '#1a1420', '#26202c', '#342a36'],
    wood: ['#2a1814', '#3c221a', '#523022'],
    edge: '#8a5a3a',
    lamp: '#ffd090',
    books: ['#5e282c', '#3a3260', '#2c4a38', '#625224'],
    glow: 0.5,
  },
  day: {
    void: ['#0e0a0a', '#1a1210', '#281c16', '#36261c', '#463222'],
    wood: ['#341c14', '#4a2a1c', '#643a26'],
    edge: '#a06a40',
    lamp: '#ffe0a8',
    books: ['#6e2a2e', '#443a6e', '#34583e', '#74602a'],
    glow: 0.3,
  },
}

export function makeLibrary(w: number, h: number, seed = 91): Library {
  const r = rng(seed)
  const towers: Tower[] = []
  // Three depths; the middle third of the screen stays clear down the centre for Shiori.
  const clear = (x: number, tw: number) => x + tw < w * 0.32 || x > w * 0.68
  for (const [layer, count, minW, maxW] of [
    [0, 16, 8, 18],
    [1, 10, 16, 30],
    [2, 5, 28, 46],
  ] as const) {
    for (let i = 0, tries = 0; i < count && tries < 200; tries++) {
      const tw = minW + Math.floor(r() * (maxW - minW))
      const x = Math.floor(r() * (w + tw)) - tw
      if (layer > 0 && !clear(x, tw)) continue
      if (towers.some((t) => t.layer === layer && x < t.x + t.w + 3 && x + tw + 3 > t.x)) continue
      const hanging = r() < (layer === 2 ? 0.35 : 0.45)
      towers.push({ x, w: tw, len: Math.round(h * (layer === 0 ? 0.3 + r() * 0.35 : 0.35 + r() * 0.4)), hanging, layer: layer as 0 | 1 | 2, seed: r() })
      i++
    }
  }
  const walkways: Library['walkways'] = []
  for (let i = 0; i < 5; i++) {
    const left = r() < 0.5
    const x0 = left ? Math.floor(r() * w * 0.1) : Math.floor(w * (0.7 + r() * 0.1))
    walkways.push({ x0, x1: x0 + Math.floor(w * (0.12 + r() * 0.12)), y: Math.round(h * (0.2 + r() * 0.55)), layer: r() < 0.5 ? 0 : 1 })
  }
  const lanterns = [0.06, 0.16, 0.26, 0.74, 0.84, 0.94].map((k) => ({ x: Math.round(w * k), drop: Math.round(h * (0.15 + r() * 0.35)), seed: r() }))
  const cranes = Array.from({ length: 4 }, () => ({ seed: r(), y: h * (0.12 + r() * 0.5), speed: 0.6 + r() * 0.6 }))
  const pages = Array.from({ length: 16 }, () => ({ x: r(), y: r(), seed: r() }))
  return { w, h, towers, walkways, lanterns, cranes, pages }
}

/** How far a tower has glided from its place: every so often it moves to a new height and rests. */
export function towerShift(t: Tower, time: number, restless: boolean): number {
  const period = restless ? 7 : 16
  const offset = t.seed * period
  const n = Math.floor((time + offset) / period)
  const p = ((time + offset) % period) / period
  const at = (k: number) => Math.round((hash1(k * 17 + t.seed * 1000) - 0.5) * (t.layer === 2 ? 24 : 16))
  const move = 0.12
  const e = p < move ? (1 - Math.cos((p / move) * Math.PI)) / 2 : 1
  return Math.round(at(n) + (at(n + 1) - at(n)) * e)
}

/** One tower drawn into its own canvas: shelves, books, lamps in its windows, its eaves. */
function drawTower(t: Tower, L: Light): HTMLCanvasElement {
  const c = document.createElement('canvas')
  c.width = t.w + 6
  c.height = t.len + 6
  const g = c.getContext('2d')!
  const x0 = 3
  const dim = [0.45, 0.75, 1][t.layer]
  const body = L.wood[t.layer === 0 ? 0 : t.layer === 1 ? 1 : 2]
  const top = t.hanging ? 0 : 6
  const len = t.len
  g.fillStyle = body
  g.fillRect(x0, top, t.w, len)
  // The eaves at the tower's free end, curving out like a castle roof.
  const eaveY = t.hanging ? len : 6
  g.fillStyle = L.wood[0]
  for (let dx = -3; dx < t.w + 3; dx++) {
    const k = Math.abs(dx - t.w / 2) / (t.w / 2 + 3)
    const lift = k > 0.85 ? 1 : 0
    g.fillRect(x0 + dx, t.hanging ? eaveY - 1 + lift : eaveY - 2 - lift, 1, 2)
  }
  g.fillStyle = L.edge
  for (let dx = -2; dx < t.w + 2; dx++) g.fillRect(x0 + dx, t.hanging ? eaveY + 1 : eaveY - 3, 1, 1)
  // Floors of shelves; near towers show their books, far ones only their lit windows.
  const floor = t.layer === 2 ? 9 : t.layer === 1 ? 7 : 5
  for (let y = top + 3; y < top + len - 3; y += floor) {
    g.fillStyle = L.wood[0]
    g.fillRect(x0, y + floor - 1, t.w, 1)
    if (t.layer > 0)
      for (let x = x0 + 2; x < x0 + t.w - 2; x++) {
        const b = hash1(x * 31 + y * 7 + t.seed * 100)
        if (b < 0.12) continue
        const bh = Math.max(1, Math.round((floor - 2) * (0.5 + b * 0.5)))
        g.fillStyle = L.books[Math.floor(b * 4)]
        g.globalAlpha = dim
        g.fillRect(x, y + floor - 1 - bh, 1, bh)
        g.globalAlpha = 1
      }
  }
  // Pillars at the edges.
  g.fillStyle = L.wood[2]
  g.fillRect(x0, top, 1, len)
  g.fillStyle = L.wood[0]
  g.fillRect(x0 + t.w - 1, top, 1, len)
  return c
}

const towerCanvases = new Map<string, HTMLCanvasElement>()

function towerCanvas(t: Tower, L: Light, hour: Hour): HTMLCanvasElement {
  const key = `${hour}:${t.x}:${t.layer}:${t.len}`
  let c = towerCanvases.get(key)
  if (!c) {
    if (towerCanvases.size > 200) towerCanvases.clear()
    c = drawTower(t, L)
    towerCanvases.set(key, c)
  }
  return c
}

/** Lamps in a tower's windows: where they are, so they can be lit and flicker each frame. */
function lamps(g: CanvasRenderingContext2D, t: Tower, x: number, y: number, L: Light, time: number) {
  const floor = t.layer === 2 ? 9 : t.layer === 1 ? 7 : 5
  const top = t.hanging ? 0 : 6
  for (let fy = top + 3; fy < top + t.len - 3; fy += floor)
    for (let fx = 3; fx < t.w - 2; fx += t.layer === 0 ? 4 : 7) {
      const k = hash1(fx * 13 + fy * 7 + t.seed * 50)
      if (k > 0.35 * L.glow + 0.05) continue
      const flick = k < 0.05 && Math.sin(time * 5 + fx) > 0.6
      g.fillStyle = flick ? '#a05a2a' : L.lamp
      g.fillRect(x + 3 + fx, y + fy + floor - 3, 1, t.layer === 2 ? 2 : 1)
    }
}

function drawStill(g: CanvasRenderingContext2D, lib: Library, L: Light) {
  const img = g.createImageData(lib.w, lib.h)
  ditherBands(img, [L.void[3], L.void[1], L.void[0], L.void[1], L.void[4]], 1)
  g.putImageData(img, 0, 0)
  // Light from far below, as if the hall went on down there.
  g.fillStyle = L.void[4]
  for (let y = Math.round(lib.h * 0.75); y < lib.h; y++)
    for (let x = 0; x < lib.w; x++) if (bayerAt(x, y) < ((y - lib.h * 0.75) / (lib.h * 0.25)) * 0.6) g.fillRect(x, y, 1, 1)
}

const still = new LayerCache()
const wells = new LayerCache()

export function drawLibrary(g: CanvasRenderingContext2D, lib: Library, time: number, calm: boolean, weather: Weather) {
  const t = calm ? 0 : time
  const L = LIBRARY_LIGHTS[weather.hour]
  const sky = weather.sky
  const restless = sky === 'Storm'
  g.drawImage(still.get('library', weather.hour, lib.w, lib.h, (c) => drawStill(c, lib, L)), 0, 0)

  for (const layer of [0, 1, 2] as const) {
    for (const tw of lib.towers) {
      if (tw.layer !== layer) continue
      const c = towerCanvas(tw, L, weather.hour)
      const dy = calm ? 0 : towerShift(tw, t, restless)
      const y = tw.hanging ? -6 + dy : lib.h - tw.len - 6 + dy
      g.drawImage(c, tw.x - 3, y)
      lamps(g, tw, tw.x - 3, y, L, t)
      // A sliding door on near towers opens now and then and lets the light out.
      if (layer === 2) {
        const open = Math.max(0, Math.sin(t * 0.25 + tw.seed * 20))
        if (open > 0.2) {
          const dx = tw.x + Math.round(tw.w / 2) - 3
          const doorY = tw.hanging ? y + tw.len - 14 : y + 12
          g.fillStyle = L.lamp
          g.globalAlpha = 0.25 + 0.6 * open * L.glow
          g.fillRect(dx, doorY, Math.max(1, Math.round(6 * open)), 8)
          g.globalAlpha = 1
        }
      }
    }
    if (layer === 1)
      for (const wk of lib.walkways) {
        g.fillStyle = L.wood[1]
        g.fillRect(wk.x0, wk.y, wk.x1 - wk.x0, 2)
        g.fillStyle = L.edge
        g.fillRect(wk.x0, wk.y, wk.x1 - wk.x0, 1)
        for (let x = wk.x0; x < wk.x1; x += 4) g.fillRect(x, wk.y - 3, 1, 3)
      }
  }

  // The dark well behind Shiori, so nothing busy stands behind her.
  g.drawImage(
    wells.get('well', weather.hour, lib.w, lib.h, (c) => {
      const cx = lib.w / 2
      const cy = lib.h * 0.42
      const rx = lib.w * 0.2
      const ry = lib.h * 0.46
      c.fillStyle = L.void[0]
      for (let y = Math.max(0, Math.round(cy - ry)); y < Math.min(lib.h, cy + ry); y++)
        for (let x = Math.round(cx - rx); x < cx + rx; x++) {
          const d = Math.hypot((x - cx) / rx, (y - cy) / ry)
          if (d < 1 && bayerAt(x, y) < (1 - d) ** 0.5 * 0.9) c.fillRect(x, y, 1, 1)
        }
    }),
    0,
    0,
  )

  // Paper lanterns on long cords, swaying slowly.
  for (const l of lib.lanterns) {
    const sway = calm ? 0 : Math.sin(t * 0.6 + l.seed * 7) * 2.5
    const x = Math.round(l.x + sway)
    g.fillStyle = '#0a0404'
    for (let y = 0; y < l.drop; y += 1) g.fillRect(Math.round(l.x + (sway * y) / l.drop), y, 1, 1)
    const flick = calm ? 1 : 0.85 + 0.15 * Math.sin(t * 7 + l.seed * 40)
    g.fillStyle = `rgba(255, 150, 70, ${0.18 * flick * (0.4 + L.glow)})`
    g.fillRect(x - 5, l.drop - 2, 11, 14)
    g.fillStyle = '#3a0c0a'
    g.fillRect(x - 2, l.drop, 5, 1)
    g.fillRect(x - 2, l.drop + 9, 5, 1)
    g.fillStyle = '#e8743a'
    g.fillRect(x - 3, l.drop + 1, 7, 8)
    g.fillStyle = '#ffc070'
    g.fillRect(x - 1, l.drop + 2, 3, 6)
    g.fillStyle = '#c84a2a'
    g.fillRect(x - 3, l.drop + 4, 7, 1)
  }

  // Paper cranes flying between the towers, around the well, never across it.
  if (sky !== 'Storm')
    for (const cr of lib.cranes) {
      const span = lib.w * 0.34
      const p = ((t * 6 * cr.speed + cr.seed * span * 2) % (span * 2)) / span
      const side = cr.seed > 0.5 ? 1 : -1
      const along = p < 1 ? p : 2 - p
      const x = Math.round(side > 0 ? lib.w * 0.68 + along * span * 0.9 : lib.w * 0.02 + along * span * 0.9)
      const y = Math.round(cr.y + Math.sin(t * 0.8 + cr.seed * 10) * 6)
      const dir = p < 1 ? 1 : -1
      const up = Math.sin(t * 6 + cr.seed * 20) > 0
      g.fillStyle = '#f0e8dc'
      g.fillRect(x, y, 3, 1)
      g.fillRect(x + (dir > 0 ? 3 : -1), y - 1, 1, 1)
      g.fillStyle = '#c8bcb0'
      g.fillRect(x + 1, y + (up ? -2 : 1), 1, up ? 2 : 1)
    }

  // Moods: dust in the lamplight, haze, pages drifting, ash.
  const drifting = sky === 'Rain' ? 16 : sky === 'Drizzle' ? 6 : sky === 'Cloudy' || sky === 'Snow' ? 12 : 0
  for (let i = 0; i < drifting; i++) {
    const p = lib.pages[i]
    const fall = sky === 'Snow' ? -5 : 7
    const y = Math.round((((p.y * lib.h + t * fall * (0.6 + p.seed)) % lib.h) + lib.h) % lib.h)
    let x = Math.round((p.x * lib.w + Math.sin(t * 0.5 + p.seed * 9) * 10) % lib.w)
    if (Math.abs(x - lib.w / 2) < lib.w * 0.2) x += x < lib.w / 2 ? -lib.w * 0.2 : lib.w * 0.2
    const flip = Math.sin(t * 2 + p.seed * 20) > 0
    g.fillStyle = sky === 'Snow' ? '#7a6a62' : sky === 'Cloudy' ? '#8a6a4a' : '#e8dcc4'
    if (sky === 'Rain' || sky === 'Drizzle') {
      g.fillRect(x, y, flip ? 4 : 2, flip ? 2 : 3)
      g.fillStyle = '#b0a490'
      g.fillRect(x, y + (flip ? 1 : 2), flip ? 4 : 2, 1)
    } else g.fillRect(x, y, 1, 1)
  }
  if (sky === 'Fog') {
    g.fillStyle = 'rgba(110, 80, 60, 0.14)'
    g.fillRect(0, 0, lib.w, lib.h)
  }
}

export function libraryScene(w: number, h: number): Scene {
  const lib = makeLibrary(w, h)
  return { w, h, draw: (g, time, calm, weather) => drawLibrary(g, lib, time, calm, weather) }
}
