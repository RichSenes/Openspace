import type { Sky } from '../../../api/types'
import { rng } from '../body/shapes'

/**
 * The first world: a city with fires on the horizon, at any hour and in any weather. Drawn at
 * the art's own size (one canvas pixel is one art pixel) and shown enlarged without smoothing.
 */
export interface Building {
  x: number
  w: number
  h: number
  /** Windows that can be lit: x, y in the building, colour index, flickers, when it lights. */
  windows: { x: number; y: number; c: number; flicker: boolean; k: number }[]
  antenna: number
}

export interface City {
  w: number
  h: number
  horizon: number
  far: Building[]
  mid: Building[]
  near: Building[]
  tower: { x: number; w: number; h: number }
  plumes: { x: number; seed: number }[]
  sign: { x: number; y: number; glyphs: number }
  stars: { x: number; y: number; k: number }[]
}

export type Hour = 'dawn' | 'day' | 'dusk' | 'night'
export const HOURS: Hour[] = ['dawn', 'day', 'dusk', 'night']
export const SKIES: Sky[] = ['Clear', 'Cloudy', 'Fog', 'Drizzle', 'Rain', 'Snow', 'Storm']

/** The hour of the world from the clock. */
export function hourOf(d: Date): Hour {
  const h = d.getHours() + d.getMinutes() / 60
  if (h >= 5.5 && h < 8) return 'dawn'
  if (h >= 8 && h < 17.5) return 'day'
  if (h >= 17.5 && h < 20.5) return 'dusk'
  return 'night'
}

/** How the city looks at an hour: its sky from the top to the horizon, its blocks, its lights. */
export interface Light {
  sky: string[]
  far: string
  mid: string
  near: string
  roof: string
  /** Share of windows lit, 0..1. */
  lit: number
  /** How strongly the fires glow on the horizon, 0..1. */
  fire: number
  stars: boolean
  rain: string
  fog: string
}

export const LIGHTS: Record<Hour, Light> = {
  night: {
    sky: ['#07060d', '#100919', '#1f0c22', '#3c1024', '#6d1a22', '#a8331e', '#d8611f'],
    far: '#1a0a19',
    mid: '#0c0510',
    near: '#09030b',
    roof: '#060207',
    lit: 1,
    fire: 1,
    stars: true,
    rain: '#2c2446',
    fog: '#2a1a30',
  },
  dusk: {
    sky: ['#120a2a', '#2a1240', '#521a4e', '#8a2a50', '#c2464a', '#e8743e', '#ffb05a'],
    far: '#3a1a3c',
    mid: '#1e0c22',
    near: '#140816',
    roof: '#0c050e',
    lit: 0.6,
    fire: 0.7,
    stars: false,
    rain: '#4a3058',
    fog: '#6a3a58',
  },
  dawn: {
    sky: ['#0d1430', '#1d2550', '#3b3a6e', '#6d4f80', '#a8687e', '#d88a78', '#f4b48a'],
    far: '#3a3456',
    mid: '#221d38',
    near: '#16122a',
    roof: '#0e0b1c',
    lit: 0.35,
    fire: 0.45,
    stars: false,
    rain: '#4a4870',
    fog: '#7a6a8a',
  },
  day: {
    sky: ['#39424f', '#4c5561', '#626a72', '#7c7a78', '#978a76', '#ad9670', '#bf9f6c'],
    far: '#5a5a62',
    mid: '#3a3a44',
    near: '#2a2932',
    roof: '#1c1b22',
    lit: 0.08,
    fire: 0.3,
    stars: false,
    rain: '#5c6270',
    fog: '#8a8a8c',
  },
}

export const WINDOW_COLOURS = ['#ffb35a', '#5fd0ff', '#ff4f9a', '#ffe7b0']

function row(r: () => number, w: number, base: number, minW: number, maxW: number, minH: number, maxH: number, lit: number): Building[] {
  const out: Building[] = []
  let x = -Math.floor(r() * maxW)
  while (x < w) {
    const bw = minW + Math.floor(r() * (maxW - minW))
    const bh = Math.floor(base * (minH + r() * (maxH - minH)))
    const windows: Building['windows'] = []
    for (let wy = 3; wy < bh - 2; wy += 3)
      for (let wx = 2; wx < bw - 2; wx += 3)
        if (r() < lit) windows.push({ x: wx, y: wy, c: r() < 0.7 ? 0 : Math.floor(r() * WINDOW_COLOURS.length), flicker: r() < 0.06, k: r() })
    out.push({ x, w: bw, h: bh, windows, antenna: r() < 0.25 ? 2 + Math.floor(r() * 6) : 0 })
    x += bw + (r() < 0.3 ? 1 + Math.floor(r() * 3) : 0)
  }
  return out
}

export function makeCity(w: number, h: number, seed = 7): City {
  const r = rng(seed)
  const horizon = Math.round(h * 0.74)
  const far = row(r, w, horizon, 5, 16, 0.08, 0.3, 0.05)
  const mid = row(r, w, horizon, 10, 26, 0.12, 0.42, 0.16)
  const near = row(r, w, h * 0.9 - horizon + Math.round(h * 0.12), 14, 34, 0.55, 1, 0.1)
  const tw = Math.max(8, Math.round(w * 0.05))
  const tower = { x: Math.round(w * 0.16), w: tw, h: Math.round(horizon * 0.78) }
  const plumes = [0.07, 0.33, 0.62, 0.9].map((k) => ({ x: Math.round(w * k + (r() - 0.5) * w * 0.05), seed: r() }))
  // The sign hangs on the side of the tallest block near the right edge.
  const pick = mid.filter((b) => b.x > w * 0.7 && b.x + b.w < w - 4).sort((a, b) => b.h - a.h)[0] ?? mid[mid.length - 1]
  const sign = { x: pick.x + pick.w, y: horizon + 2 - pick.h + 3, glyphs: 4 }
  const stars = Array.from({ length: Math.round(w * 0.12) }, () => ({ x: Math.floor(r() * w), y: Math.floor(r() * horizon * 0.5), k: r() }))
  return { w, h, horizon, far, mid, near, tower, plumes, sign, stars }
}

/** Glyphs of the neon sign: made-up characters, 5×5, readable as signage, not as words. */
export const SIGN_GLYPHS: string[][] = [
  ['#####', '#...#', '#####', '#...#', '#####'],
  ['..#..', '#####', '..#..', '.#.#.', '#...#'],
  ['#####', '#.#.#', '#####', '#.#.#', '#####'],
  ['.#...', '#####', '.#..#', '.#..#', '.###.'],
  ['#####', '....#', '.###.', '....#', '#####'],
]

const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5]
export const bayerAt = (x: number, y: number) => (BAYER[(y & 3) * 4 + (x & 3)] + 0.5) / 16

function hex(c: string): [number, number, number] {
  const n = parseInt(c.slice(1), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

/** The sky: bands of the hour's palette blended with an ordered dither, never a smooth gradient. */
export function drawSky(g: CanvasRenderingContext2D, city: City, light: Light, overcast: boolean) {
  const { w, horizon } = city
  const img = g.createImageData(w, horizon)
  // Clouds flatten the sky toward its middle tones.
  const palette = overcast ? light.sky.slice(2, 6) : light.sky
  const cols = palette.map(hex)
  for (let y = 0; y < horizon; y++) {
    const f = (y / horizon) ** 1.7 * (cols.length - 1)
    const i = Math.floor(f)
    const k = f - i
    for (let x = 0; x < w; x++) {
      const c = cols[Math.min(cols.length - 1, k > bayerAt(x, y) ? i + 1 : i)]
      const o = (y * w + x) * 4
      img.data[o] = c[0]
      img.data[o + 1] = c[1]
      img.data[o + 2] = c[2]
      img.data[o + 3] = 255
    }
  }
  g.putImageData(img, 0, 0)
}

function building(g: CanvasRenderingContext2D, b: Building, base: number, colour: string, time: number, seed: number, lit: number) {
  g.fillStyle = colour
  g.fillRect(b.x, base - b.h, b.w, b.h)
  if (b.antenna) g.fillRect(b.x + Math.floor(b.w / 2), base - b.h - b.antenna, 1, b.antenna)
  b.windows.forEach((win, i) => {
    if (win.k > lit) return
    if (win.flicker && Math.sin(time * 7 + i * 13 + seed) > 0.3) return
    g.fillStyle = WINDOW_COLOURS[win.c]
    g.fillRect(b.x + win.x, base - b.h + win.y, 1, 1)
  })
}

export interface Weather {
  hour: Hour
  sky: Sky
}

/** How much falls from the sky, in streaks per 100 columns. */
export function rainAmount(sky: Sky): number {
  return sky === 'Storm' ? 70 : sky === 'Rain' ? 36 : sky === 'Drizzle' ? 14 : 0
}

/**
 * One frame of the city over a sky drawn once by the caller. `time` is in seconds; `calm`
 * stops the motion.
 */
export function drawCity(g: CanvasRenderingContext2D, city: City, sky: HTMLCanvasElement, time: number, calm: boolean, weather: Weather) {
  const { w, h, horizon } = city
  const light = LIGHTS[weather.hour]
  const t = calm ? 0 : time
  g.drawImage(sky, 0, 0)

  if (light.stars && (weather.sky === 'Clear' || weather.sky === 'Cloudy'))
    for (const s of city.stars) {
      if (weather.sky === 'Cloudy' && s.k > 0.3) continue
      g.fillStyle = Math.sin(t * (1 + s.k * 2) + s.k * 40) > 0.6 ? '#ffffff' : '#8a84b0'
      g.fillRect(s.x, s.y, 1, 1)
    }

  // Lightning lights the whole sky for a moment, then a bolt.
  const flash = weather.sky === 'Storm' && !calm ? stormFlash(t) : 0
  if (flash > 0) {
    g.fillStyle = `rgba(200, 190, 255, ${0.35 * flash})`
    g.fillRect(0, 0, w, horizon)
    if (flash > 0.6) bolt(g, city, Math.floor(t / 7))
  }

  // Fire on the horizon: a band that breathes.
  for (let i = 0; i < 4; i++) {
    const glow = 0.5 + 0.5 * Math.sin(t * (1.3 + i * 0.7) + i * 2)
    g.fillStyle = i % 2 ? '#ff8a2a' : '#ffb04a'
    for (let x = (i * 3) % 4; x < w; x += 4) {
      const lift = Math.round((2 + 3 * glow * (0.5 + 0.5 * Math.sin(x * 0.21 + i + t * 0.8))) * light.fire)
      if (lift > 0) g.fillRect(x, horizon - lift - i, 1, 1)
    }
  }

  // Smoke rising from the fires, in dithered puffs.
  g.fillStyle = weather.hour === 'day' ? '#4a4246' : '#2a1020'
  for (const p of city.plumes) {
    for (let k = 0; k < 40; k++) {
      const age = (t * 0.05 + k / 40 + p.seed) % 1
      const y = horizon - age * horizon * 0.9
      const x = p.x + Math.sin(age * 6 + p.seed * 9) * 6 + age * 18
      const rad = 2 + age * 8
      // Thick at the base, thinner as it rises: fewer dither steps let it through.
      const density = 0.75 * (1 - age) ** 0.8
      for (let dy = -rad; dy <= rad; dy++)
        for (let dx = -rad; dx <= rad; dx++) {
          const d = (dx * dx + dy * dy) / (rad * rad)
          if (d > 1) continue
          const px = Math.round(x + dx)
          const py = Math.round(y + dy)
          if (py > 0 && bayerAt(px, py) < density * (1 - d * 0.6)) g.fillRect(px, py, 1, 1)
        }
    }
  }

  for (const b of city.far) building(g, b, horizon, light.far, t, 3, light.lit)

  // The tower and its beacon.
  const tw = city.tower
  g.fillStyle = light.mid
  g.fillRect(tw.x, horizon - tw.h, tw.w, tw.h)
  g.fillRect(tw.x + Math.floor(tw.w / 2) - 1, horizon - tw.h - 8, 2, 8)
  g.fillStyle = light.far
  for (let y = horizon - tw.h + 4; y < horizon; y += 6) g.fillRect(tw.x + 1, y, tw.w - 2, 1)
  if (Math.sin(t * 3) > 0) {
    g.fillStyle = '#ff2a3a'
    g.fillRect(tw.x + Math.floor(tw.w / 2) - 1, horizon - tw.h - 9, 2, 2)
  }

  if (weather.sky === 'Fog') fog(g, city, light.fog, t, horizon * 0.45, 0.45)

  for (const b of city.mid) building(g, b, horizon + 2, light.mid, t, 7, light.lit)
  sign(g, city, t, calm, weather.hour)

  // The streets below the horizon: closer, darker blocks.
  const roofY = Math.round(h * 0.9)
  g.fillStyle = light.near
  g.fillRect(0, horizon + 2, w, roofY - horizon)
  for (const b of city.near) building(g, b, roofY, light.near, t, 11, light.lit)

  if (weather.sky === 'Fog') fog(g, city, light.fog, t * 1.4, horizon * 0.8, 0.6)

  // Embers drifting up; fewer by day.
  const embers = Math.round(46 * light.fire)
  for (let i = 0; i < embers; i++) {
    const seed = (i * 0.618) % 1
    const age = (t * (0.05 + seed * 0.05) + seed) % 1
    const x = Math.round(((seed * 997) % w) + Math.sin(age * 9 + i) * 4)
    const y = Math.round(horizon - age * horizon * 0.8)
    g.fillStyle = age < 0.6 ? '#ffb04a' : '#a8331e'
    g.fillRect(x, y, 1, 1)
  }

  const rain = Math.round((rainAmount(weather.sky) * w) / 100)
  if (rain) {
    g.fillStyle = light.rain
    const slant = weather.sky === 'Storm' ? 60 : 30
    const len = weather.sky === 'Drizzle' ? 1 : weather.sky === 'Storm' ? 3 : 2
    for (let i = 0; i < rain; i++) {
      const seed = (i * 0.754877) % 1
      const x = Math.round((seed * w + t * slant) % w)
      const y = Math.round(((((i * 0.5698) % 1) * h + t * (weather.sky === 'Drizzle' ? 50 : 90)) % h))
      g.fillRect(x, y, 1, len)
    }
  }

  if (weather.sky === 'Snow')
    for (let i = 0; i < Math.round(w * 0.35); i++) {
      const seed = (i * 0.754877) % 1
      const fall = 8 + seed * 10
      const y = Math.round((((i * 0.5698) % 1) * h + t * fall) % h)
      const x = Math.round((seed * w + Math.sin(t * 0.8 + i) * 3) % w)
      g.fillStyle = seed > 0.7 ? '#ffffff' : '#c8c4dc'
      g.fillRect(x, y, 1, 1)
    }

  // The roof we stand on.
  const roof = Math.round(h * 0.9)
  g.fillStyle = light.roof
  g.fillRect(0, roof, w, h - roof)
  g.fillRect(0, roof - 9, w, 1)
  for (let x = 2; x < w; x += 9) g.fillRect(x, roof - 9, 1, 9)
  g.fillStyle = weather.sky === 'Snow' ? '#5a5670' : '#1a0d1c'
  g.fillRect(0, roof, w, 1)
}

/** 0..1: a storm's flash, twice in quick succession every few seconds. */
export function stormFlash(t: number): number {
  const p = t % 7
  if (p < 0.08) return 1
  if (p > 0.2 && p < 0.26) return 0.7
  return 0
}

function bolt(g: CanvasRenderingContext2D, city: City, n: number) {
  const r = rng(100 + n)
  let x = Math.round(city.w * (0.2 + r() * 0.6))
  g.fillStyle = '#f2eeff'
  for (let y = 0; y < city.horizon * 0.8; y++) {
    if (r() < 0.3) x += r() < 0.5 ? -1 : 1
    g.fillRect(x, y, 1, 1)
  }
}

/** Fog: a dithered band that drifts, denser at its middle. */
function fog(g: CanvasRenderingContext2D, city: City, colour: string, t: number, centre: number, density: number) {
  g.fillStyle = colour
  const span = city.h * 0.22
  for (let y = Math.max(0, Math.round(centre - span)); y < Math.min(city.h, centre + span); y++) {
    const k = (1 - Math.abs(y - centre) / span) * density
    const drift = t * 3
    for (let x = 0; x < city.w; x++) {
      const wisp = 0.55 + 0.25 * Math.sin((x + drift) * 0.045 + y * 0.09) + 0.2 * Math.sin((x - drift * 0.6) * 0.11 - y * 0.05)
      if (bayerAt(x, y) < k * wisp) g.fillRect(x, y, 1, 1)
    }
  }
}

/** A vertical neon sign on the side of a block, with a frame, glyphs and a stutter. */
function sign(g: CanvasRenderingContext2D, city: City, t: number, calm: boolean, hour: Hour) {
  const s = city.sign
  const height = s.glyphs * 7 + 3
  g.fillStyle = '#1a0a18'
  g.fillRect(s.x, s.y - 1, 1, 2)
  g.fillRect(s.x, s.y + height - 2, 1, 2)
  g.fillStyle = '#12060f'
  g.fillRect(s.x + 1, s.y - 2, 9, height + 2)
  const on = calm || Math.sin(t * 11) > -0.85 || Math.sin(t * 2.3) > 0.4
  const dim = hour === 'day'
  g.fillStyle = on ? (dim ? '#a04070' : '#ff3d8f') : '#4a1a34'
  g.fillRect(s.x + 1, s.y - 2, 9, 1)
  g.fillRect(s.x + 1, s.y + height - 1, 9, 1)
  for (let i = 0; i < s.glyphs; i++) {
    const glyph = SIGN_GLYPHS[(i + 1) % SIGN_GLYPHS.length]
    // One glyph dies now and then, as old signs do.
    const dead = !calm && i === 2 && Math.sin(t * 5.3) > 0.2
    g.fillStyle = on && !dead ? (dim ? '#c05088' : '#ff6fb0') : '#3a1428'
    glyph.forEach((rowBits, y) =>
      [...rowBits].forEach((c, x) => {
        if (c === '#') g.fillRect(s.x + 3 + x, s.y + 1 + i * 7 + y, 1, 1)
      }),
    )
  }
}
