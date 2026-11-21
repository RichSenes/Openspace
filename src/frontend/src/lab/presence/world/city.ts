import type { Sky } from '../../../api/types'
import { rng } from '../body/shapes'
import { bayerAt, disc, ditherBands, glyph, hash1 } from './pixels'
import type { Hour, Scene, Weather } from './types'

export { HOURS, hourOf, SKIES } from './types'
export type { Hour, Weather } from './types'

/**
 * Two cities on the same bones: the burning city (fires on the horizon) and Tokyo (its tower,
 * its sky tree, a train on a viaduct, signs everywhere). Any hour, any weather, and the weather
 * shows in the details: clouds, the moon or the sun, puddles that hold the neon, snow on every
 * ledge, blackouts in a storm.
 */
export type CityKind = 'burning' | 'tokyo'

export interface Building {
  x: number
  w: number
  h: number
  /** Windows that can be lit: x, y in the building, colour index, flickers, when it lights. */
  windows: { x: number; y: number; c: number; flicker: boolean; k: number }[]
  antenna: number
}

export interface Sign {
  x: number
  y: number
  glyphs: number
  colour: number
  /** Which side of its block it hangs from. */
  side: 1 | -1
}

export interface City {
  kind: CityKind
  w: number
  h: number
  horizon: number
  roof: number
  far: Building[]
  mid: Building[]
  near: Building[]
  tower: { x: number; w: number; h: number }
  plumes: { x: number; seed: number }[]
  signs: Sign[]
  stars: { x: number; y: number; k: number }[]
  clouds: { x: number; y: number; w: number; h: number; speed: number; seed: number }[]
  puddles: { x: number; w: number; y: number; c: number }[]
  /** Tokyo only. */
  rail?: { y: number }
  billboard?: { x: number; y: number; w: number; h: number }
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
  cloud: string
  snow: string
}

export const LIGHTS: Record<Hour, Light> = {
  night: {
    sky: ['#07060d', '#100919', '#1f0c22', '#3c1024', '#6d1a22', '#a8331e', '#d8611f'],
    far: '#1a0a19', mid: '#0c0510', near: '#09030b', roof: '#060207',
    lit: 1, fire: 1, stars: true, rain: '#2c2446', fog: '#2a1a30', cloud: '#1a0f20', snow: '#8a86a8',
  },
  dusk: {
    sky: ['#120a2a', '#2a1240', '#521a4e', '#8a2a50', '#c2464a', '#e8743e', '#ffb05a'],
    far: '#3a1a3c', mid: '#1e0c22', near: '#140816', roof: '#0c050e',
    lit: 0.6, fire: 0.7, stars: false, rain: '#4a3058', fog: '#6a3a58', cloud: '#4a2038', snow: '#c8a8c0',
  },
  dawn: {
    sky: ['#0d1430', '#1d2550', '#3b3a6e', '#6d4f80', '#a8687e', '#d88a78', '#f4b48a'],
    far: '#3a3456', mid: '#221d38', near: '#16122a', roof: '#0e0b1c',
    lit: 0.35, fire: 0.45, stars: false, rain: '#4a4870', fog: '#7a6a8a', cloud: '#4a4068', snow: '#c8c4e0',
  },
  day: {
    sky: ['#39424f', '#4c5561', '#626a72', '#7c7a78', '#978a76', '#ad9670', '#bf9f6c'],
    far: '#5a5a62', mid: '#3a3a44', near: '#2a2932', roof: '#1c1b22',
    lit: 0.08, fire: 0.3, stars: false, rain: '#5c6270', fog: '#8a8a8c', cloud: '#55565e', snow: '#dcdce4',
  },
}

/** Tokyo has no fires: its horizon glows with the city itself. */
export const TOKYO_LIGHTS: Record<Hour, Light> = {
  night: { ...LIGHTS.night, sky: ['#05061a', '#0a0d2c', '#141846', '#24205a', '#3e2468', '#6a2a78', '#a03a88'], far: '#141434', mid: '#0a0a22', near: '#07071a', roof: '#040412', fire: 0, cloud: '#16163a' },
  dusk: { ...LIGHTS.dusk, sky: ['#141238', '#2a1a58', '#4e2468', '#7e3070', '#b4466e', '#e2706a', '#ffa86e'], fire: 0 },
  dawn: { ...LIGHTS.dawn, fire: 0 },
  day: { ...LIGHTS.day, sky: ['#4a6a8c', '#5c7c9c', '#7090aa', '#88a2b4', '#a2b2bc', '#b8c0c2', '#c8c8c2'], far: '#6a7486', mid: '#4a5262', near: '#343a48', fire: 0, cloud: '#c8ccd4' },
}

export const WINDOW_COLOURS = ['#ffb35a', '#5fd0ff', '#ff4f9a', '#ffe7b0']
const NEON = ['#ff3d8f', '#3de0ff', '#ffd23d', '#6dff9a']

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

export function makeCity(w: number, h: number, seed = 7, kind: CityKind = 'burning'): City {
  const r = rng(seed)
  const horizon = Math.round(h * 0.74)
  const roof = Math.round(h * 0.9)
  const tokyo = kind === 'tokyo'
  const far = row(r, w, horizon, 5, 16, tokyo ? 0.14 : 0.08, tokyo ? 0.4 : 0.3, tokyo ? 0.12 : 0.05)
  const mid = row(r, w, horizon, 10, 26, 0.12, tokyo ? 0.5 : 0.42, tokyo ? 0.24 : 0.16)
  const near = row(r, w, roof - horizon + Math.round(h * 0.12), 14, 34, 0.55, 1, 0.1)
  const tw = Math.max(8, Math.round(w * 0.05))
  // Tokyo's tower stands far away, behind the distant blocks: a detail of the skyline.
  const tower = tokyo
    ? { x: Math.round(w * 0.3), w: Math.max(4, Math.round(horizon * 0.68 * 0.08)), h: Math.round(horizon * 0.68) }
    : { x: Math.round(w * 0.16), w: tw, h: Math.round(horizon * 0.78) }
  const clearOfTower = (b: Building) => !tokyo || b.x + b.w + 4 < tower.x - tower.w || b.x - 4 > tower.x + tower.w
  const plumes = tokyo ? [] : [0.07, 0.33, 0.62, 0.9].map((k) => ({ x: Math.round(w * k + (r() - 0.5) * w * 0.05), seed: r() }))
  // Signs hang on the side of tall blocks: one in the burning city, many in Tokyo.
  const tall = mid.filter((b) => b.x > 6 && b.x + b.w < w - 12 && b.h > horizon * 0.18 && clearOfTower(b))
  const chosen = tokyo
    ? tall.filter((_, i) => i % 2 === 0).slice(0, 7)
    : [tall.filter((b) => b.x > w * 0.7).sort((a, b) => b.h - a.h)[0] ?? mid[mid.length - 1]]
  const signs: Sign[] = chosen.map((b, i) => {
    const side: 1 | -1 = tokyo && i % 2 ? -1 : 1
    return { x: side > 0 ? b.x + b.w : b.x - 10, y: horizon + 2 - b.h + 3, glyphs: tokyo ? 2 + Math.floor(r() * 3) : 4, colour: tokyo ? i % NEON.length : 0, side }
  })
  const stars = Array.from({ length: Math.round(w * 0.12) }, () => ({ x: Math.floor(r() * w), y: Math.floor(r() * horizon * 0.5), k: r() }))
  const clouds = Array.from({ length: 7 }, () => ({ x: r() * w, y: horizon * (0.08 + r() * 0.45), w: w * (0.18 + r() * 0.25), h: horizon * (0.05 + r() * 0.06), speed: 1 + r() * 2, seed: r() }))
  const puddles = Array.from({ length: Math.round(w / 18) }, () => ({ x: Math.floor(r() * w), w: 3 + Math.floor(r() * 9), y: roof + 2 + Math.floor(r() * Math.max(1, h - roof - 4)), c: Math.floor(r() * NEON.length) }))
  const city: City = { kind, w, h, horizon, roof, far, mid, near, tower, plumes, signs, stars, clouds, puddles }
  if (tokyo) {
    city.rail = { y: Math.round(horizon + (roof - horizon) * 0.32) }
    const host = tall.find((b) => b.x > w * 0.04 && b.x < w * 0.3 && b.w >= 18) ?? tall[0]
    if (host) city.billboard = { x: host.x + 2, y: horizon + 2 - host.h + 4, w: Math.min(host.w - 4, 26), h: 12 }
  }
  return city
}

/** Glyphs of the neon signs: made-up characters, 5×5, readable as signage, not as words. */
export const SIGN_GLYPHS: string[][] = [
  ['#####', '#...#', '#####', '#...#', '#####'],
  ['..#..', '#####', '..#..', '.#.#.', '#...#'],
  ['#####', '#.#.#', '#####', '#.#.#', '#####'],
  ['.#...', '#####', '.#..#', '.#..#', '.###.'],
  ['#####', '....#', '.###.', '....#', '#####'],
  ['#.#.#', '#####', '..#..', '.###.', '#.#.#'],
]

export { bayerAt } from './pixels'

/** The sky: bands of the hour's palette blended with an ordered dither, never a smooth gradient. */
export function drawSky(g: CanvasRenderingContext2D, city: City, light: Light, overcast: boolean) {
  const img = g.createImageData(city.w, city.horizon)
  ditherBands(img, overcast ? light.sky.slice(2, 6) : light.sky)
  g.putImageData(img, 0, 0)
}

function building(g: CanvasRenderingContext2D, b: Building, base: number, colour: string, time: number, seed: number, lit: number, dark: (x: number) => boolean) {
  g.fillStyle = colour
  g.fillRect(b.x, base - b.h, b.w, b.h)
  if (b.antenna) g.fillRect(b.x + Math.floor(b.w / 2), base - b.h - b.antenna, 1, b.antenna)
  b.windows.forEach((win, i) => {
    if (win.k > lit || dark(b.x + win.x)) return
    if (win.flicker && Math.sin(time * 7 + i * 13 + seed) > 0.3) return
    g.fillStyle = WINDOW_COLOURS[win.c]
    g.fillRect(b.x + win.x, base - b.h + win.y, 1, 1)
  })
}

/** Snow on the top of every block of a row. */
function caps(g: CanvasRenderingContext2D, row: Building[], base: number, colour: string) {
  g.fillStyle = colour
  for (const b of row) {
    g.fillRect(b.x, base - b.h - 1, b.w, 1)
    g.fillRect(b.x + 1, base - b.h - 2, b.w - 2, 1)
  }
}

/** Lightning catches the top edges of a row. */
function edges(g: CanvasRenderingContext2D, row: Building[], base: number, colour: string) {
  g.fillStyle = colour
  for (const b of row) g.fillRect(b.x, base - b.h, b.w, 1)
}

/** How much falls from the sky, in streaks per 100 columns. */
export function rainAmount(sky: Sky): number {
  return sky === 'Storm' ? 70 : sky === 'Rain' ? 36 : sky === 'Drizzle' ? 14 : 0
}

/** 0..1: a storm's flash, twice in quick succession every few seconds. */
export function stormFlash(t: number): number {
  const p = t % 7
  if (p < 0.08) return 1
  if (p > 0.2 && p < 0.26) return 0.7
  return 0
}

/** In a storm, now and then a district loses power: the columns it covers, or null. */
export function blackout(t: number, w: number): [number, number] | null {
  const n = Math.floor(t / 5)
  if (hash1(n) < 0.55) return null
  const from = Math.floor(hash1(n + 99) * w * 0.8)
  return [from, from + Math.floor(w * (0.15 + hash1(n + 7) * 0.2))]
}

function bolt(g: CanvasRenderingContext2D, city: City, n: number) {
  const r = rng(100 + n)
  let x = Math.round(city.w * (0.2 + r() * 0.6))
  g.fillStyle = '#f2eeff'
  for (let y = 0; y < city.horizon * 0.8; y++) {
    if (r() < 0.3) x += r() < 0.5 ? -1 : 1
    g.fillRect(x, y, 1, 1)
    if (r() < 0.03) {
      let bx = x
      for (let k = 0; k < 10; k++) g.fillRect((bx += r() < 0.5 ? -1 : 1), y + k, 1, 1)
    }
  }
}

/** Fog: a dithered band that drifts, denser at its middle. */
function fog(g: CanvasRenderingContext2D, city: City, colour: string, t: number, centre: number, density: number) {
  g.fillStyle = colour
  const span = city.h * 0.22
  const drift = t * 3
  for (let y = Math.max(0, Math.round(centre - span)); y < Math.min(city.h, centre + span); y++) {
    const k = (1 - Math.abs(y - centre) / span) * density
    for (let x = 0; x < city.w; x++) {
      const wisp = 0.55 + 0.25 * Math.sin((x + drift) * 0.045 + y * 0.09) + 0.2 * Math.sin((x - drift * 0.6) * 0.11 - y * 0.05)
      if (bayerAt(x, y) < k * wisp) g.fillRect(x, y, 1, 1)
    }
  }
}

/** Clouds as dithered lenses that drift; thicker and darker in bad weather. */
function clouds(g: CanvasRenderingContext2D, city: City, colour: string, t: number, density: number) {
  g.fillStyle = colour
  for (const c of city.clouds) {
    const x0 = ((c.x + t * c.speed) % (city.w + c.w)) - c.w
    for (let y = Math.round(c.y - c.h); y < c.y + c.h; y++) {
      const v = 1 - Math.abs(y - c.y) / c.h
      const half = (c.w / 2) * Math.sqrt(Math.max(0, v))
      for (let x = Math.round(x0 + c.w / 2 - half); x < x0 + c.w / 2 + half; x++) {
        const lump = 0.6 + 0.4 * Math.sin(x * 0.2 + c.seed * 30 + y * 0.3)
        if (bayerAt(x, y) < v * density * lump) g.fillRect(x, y, 1, 1)
      }
    }
  }
}

function sign(g: CanvasRenderingContext2D, s: Sign, t: number, calm: boolean, hour: Hour, dead: boolean) {
  const height = s.glyphs * 7 + 3
  const colour = NEON[s.colour]
  g.fillStyle = '#1a0a18'
  const arm = s.side > 0 ? s.x : s.x + 9
  g.fillRect(arm, s.y - 1, 1, 2)
  g.fillRect(arm, s.y + height - 2, 1, 2)
  const bx = s.side > 0 ? s.x + 1 : s.x
  g.fillStyle = '#12060f'
  g.fillRect(bx, s.y - 2, 9, height + 2)
  const on = !dead && (calm || Math.sin(t * 11 + s.colour * 3) > -0.85 || Math.sin(t * 2.3 + s.colour) > 0.4)
  const dim = hour === 'day'
  g.fillStyle = on ? colour : '#3a1428'
  if (on && dim) g.globalAlpha = 0.6
  g.fillRect(bx, s.y - 2, 9, 1)
  g.fillRect(bx, s.y + height - 1, 9, 1)
  for (let i = 0; i < s.glyphs; i++) {
    const broken = !calm && i === 2 && Math.sin(t * 5.3 + s.colour) > 0.2
    g.fillStyle = on && !broken ? colour : '#3a1428'
    glyph(g, SIGN_GLYPHS[(i + 1 + s.colour) % SIGN_GLYPHS.length], bx + 2, s.y + 1 + i * 7)
  }
  g.globalAlpha = 1
}

/**
 * Tokyo's tower, far away: a small red lattice silhouette with its two decks and a blinking
 * light, behind the distant blocks. Seen through the haze, so its colours are muted.
 */
function tokyoTower(g: CanvasRenderingContext2D, city: City, hour: Hour, t: number) {
  const base = city.horizon + 1
  const height = city.tower.h
  const cx = city.tower.x
  const foot = city.tower.w
  const night = hour === 'night' || hour === 'dusk'
  g.fillStyle = night ? '#6a2228' : '#9a3a32'
  for (let y = 0; y < height; y++) {
    const k = y / height
    const hw = k > 0.8 ? 0 : Math.round(foot * (1 - k / 0.8) ** 1.3)
    g.fillRect(cx - hw, base - y, hw * 2 + 1, 1)
  }
  for (const k of [0.42, 0.66]) {
    const hw = Math.round(foot * (1 - k / 0.8) ** 1.3) + 1
    g.fillRect(cx - hw, base - Math.round(height * k), hw * 2 + 1, 1)
  }
  if (night && Math.sin(t * 2.5) > 0.3) {
    g.fillStyle = '#ff3a3a'
    g.fillRect(cx, base - height, 1, 1)
  }
}

/** The sky tree: very tall, very thin, two pods, lit blue or purple at night. */
function skyTree(g: CanvasRenderingContext2D, city: City, hour: Hour, light: Light) {
  const base = city.horizon + 1
  const height = Math.round(city.horizon * 0.9)
  const cx = Math.round(city.w * 0.74)
  const night = hour === 'night' || hour === 'dusk'
  for (let y = 0; y < height; y++) {
    const hw = Math.max(0, Math.round(4 * (1 - y / height) ** 0.8))
    g.fillStyle = light.far
    g.fillRect(cx - hw, base - y, hw * 2 + 1, 1)
    if (night && y % 3 === 0) {
      g.fillStyle = Math.floor(y / 30) % 2 ? '#7a6aff' : '#5fd0ff'
      g.fillRect(cx - hw, base - y, 1, 1)
      g.fillRect(cx + hw, base - y, 1, 1)
    }
  }
  for (const k of [0.55, 0.78]) {
    const y = base - Math.round(height * k)
    g.fillStyle = light.far
    g.fillRect(cx - 4, y - 2, 9, 3)
    if (night) {
      g.fillStyle = '#c8d8ff'
      for (let x = -3; x <= 3; x += 2) g.fillRect(cx + x, y - 1, 1, 1)
    }
  }
}

/** The elevated line and a train that crosses now and then. */
function railway(g: CanvasRenderingContext2D, city: City, light: Light, t: number, calm: boolean) {
  const y = city.rail!.y
  g.fillStyle = light.mid
  g.fillRect(0, y + 4, city.w, 2)
  for (let x = 4; x < city.w; x += 22) g.fillRect(x, y + 6, 2, city.roof - y)
  const period = 13
  const p = calm ? 0.4 : (t % period) / period
  const length = 4 * 24
  const x0 = Math.round(p * (city.w + length + 40)) - length - 20
  for (let car = 0; car < 4; car++) {
    const x = x0 + car * 24
    g.fillStyle = '#8a96a0'
    g.fillRect(x, y, 22, 4)
    g.fillStyle = '#3aa060'
    g.fillRect(x, y + 3, 22, 1)
    g.fillStyle = light.lit > 0.3 ? '#fff2c8' : '#c0d0d8'
    for (let wx = 2; wx < 21; wx += 3) g.fillRect(x + wx, y + 1, 2, 1)
  }
}

/** The billboard: an animated screen that cycles colours, like a giant advert with no words. */
function billboard(g: CanvasRenderingContext2D, city: City, t: number) {
  const b = city.billboard!
  g.fillStyle = '#05050a'
  g.fillRect(b.x - 1, b.y - 1, b.w + 2, b.h + 2)
  const phase = Math.floor(t * 2) % 3
  for (let y = 0; y < b.h; y++)
    for (let x = 0; x < b.w; x++) {
      let on: boolean
      if (phase === 0) on = Math.sin(x * 0.5 + t * 6) + Math.sin(y * 0.7) > 0.3
      else if (phase === 1) on = Math.hypot(x - b.w / 2, (y - b.h / 2) * 1.6) < 4 + 3 * Math.sin(t * 4)
      else on = (x + y + Math.floor(t * 12)) % 6 < 2
      if (!on) continue
      g.fillStyle = ['#ff3d8f', '#3de0ff', '#ffd23d'][(phase + Math.floor(x / 8)) % 3]
      g.fillRect(b.x + x, b.y + y, 1, 1)
    }
}

/**
 * One frame of the city over a sky drawn once by the caller. `time` is in seconds; `calm`
 * stops the motion.
 */
export function drawCity(g: CanvasRenderingContext2D, city: City, sky: HTMLCanvasElement, time: number, calm: boolean, weather: Weather) {
  const { w, h, horizon, roof } = city
  const light = (city.kind === 'tokyo' ? TOKYO_LIGHTS : LIGHTS)[weather.hour]
  const t = calm ? 0 : time
  const wet = weather.sky === 'Rain' || weather.sky === 'Storm' || weather.sky === 'Drizzle'
  const snowy = weather.sky === 'Snow'
  const clear = weather.sky === 'Clear'
  g.drawImage(sky, 0, 0)

  if (light.stars && (clear || weather.sky === 'Cloudy'))
    for (const s of city.stars) {
      if (!clear && s.k > 0.3) continue
      g.fillStyle = Math.sin(t * (1 + s.k * 2) + s.k * 40) > 0.6 ? '#ffffff' : '#8a84b0'
      g.fillRect(s.x, s.y, 1, 1)
    }

  // The moon by night, the sun by day, when the sky lets them through.
  if (clear || weather.sky === 'Cloudy') {
    if (weather.hour === 'night') {
      const mx = Math.round(w * 0.82)
      const my = Math.round(horizon * 0.2)
      const mr = Math.max(4, Math.round(h * 0.035))
      g.fillStyle = '#3a2a48'
      disc(g, mx, my, mr, mr * 0.9)
      g.fillStyle = '#e8e4f8'
      disc(g, mx, my, mr)
      g.fillStyle = '#b8b0d0'
      for (const [dx, dy, r] of [
        [-0.3, -0.2, 0.22],
        [0.25, 0.3, 0.18],
        [0.3, -0.35, 0.12],
      ]) disc(g, mx + dx * mr, my + dy * mr, Math.max(1, r * mr))
    } else {
      const low = weather.hour !== 'day'
      const sx = Math.round(w * (weather.hour === 'dawn' ? 0.2 : weather.hour === 'dusk' ? 0.78 : 0.62))
      const sy = Math.round(horizon * (low ? 0.82 : 0.22))
      const sr = Math.max(5, Math.round(h * (low ? 0.06 : 0.04)))
      g.fillStyle = low ? '#ff9a4a' : '#e8d8b0'
      disc(g, sx, sy, sr, sr * 1.4)
      g.fillStyle = low ? '#ffd08a' : '#fff4d8'
      disc(g, sx, sy, sr * 0.75)
    }
  }

  const cloudDensity = weather.sky === 'Cloudy' ? 0.8 : weather.sky === 'Storm' ? 1 : weather.sky === 'Rain' || snowy ? 0.7 : weather.sky === 'Drizzle' ? 0.5 : 0
  if (cloudDensity) clouds(g, city, weather.sky === 'Storm' ? '#0a0610' : light.cloud, t, cloudDensity)

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
      // Wind in a storm bends the smoke over.
      const x = p.x + Math.sin(age * 6 + p.seed * 9) * 6 + age * (weather.sky === 'Storm' ? 60 : 18)
      const rad = 2 + age * 8
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

  const out = weather.sky === 'Storm' && !calm ? blackout(t, w) : null
  const dark = (x: number) => !!out && x >= out[0] && x < out[1]

  if (city.kind === 'tokyo') {
    skyTree(g, city, weather.hour, light)
    tokyoTower(g, city, weather.hour, t)
  }
  for (const b of city.far) building(g, b, horizon, light.far, t, 3, light.lit, dark)
  if (snowy) caps(g, city.far, horizon, light.snow)

  if (city.kind === 'burning') {
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
    if (snowy) caps(g, [{ x: tw.x, w: tw.w, h: tw.h, windows: [], antenna: 0 }], horizon, light.snow)
  }

  if (weather.sky === 'Fog') fog(g, city, light.fog, t, horizon * 0.45, 0.5)

  for (const b of city.mid) building(g, b, horizon + 2, light.mid, t, 7, light.lit, dark)
  if (snowy) caps(g, city.mid, horizon + 2, light.snow)
  if (flash > 0.6) edges(g, city.mid, horizon + 2, '#b8b0e8')
  city.signs.forEach((s, i) => sign(g, s, t, calm, weather.hour, dark(s.x) || (weather.sky === 'Storm' && i === 0 && Math.sin(t * 0.7) > 0.5)))
  if (city.billboard) billboard(g, city, t)

  // The streets below the horizon: closer, darker blocks.
  g.fillStyle = light.near
  g.fillRect(0, horizon + 2, w, roof - horizon)
  if (city.rail) railway(g, city, light, t, calm)
  for (const b of city.near) building(g, b, roof, light.near, t, 11, light.lit, dark)
  if (snowy) caps(g, city.near, roof, light.snow)

  if (weather.sky === 'Fog') fog(g, city, light.fog, t * 1.4, horizon * 0.8, 0.65)

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

  // Petals on a clear day in Tokyo.
  if (city.kind === 'tokyo' && clear && weather.hour !== 'night') {
    g.fillStyle = '#ffb8d0'
    for (let i = 0; i < 40; i++) {
      const s = hash1(i)
      const x = Math.round((s * w + t * (8 + s * 6) + Math.sin(t + i) * 5) % w)
      const y = Math.round((hash1(i + 50) * h + t * (6 + s * 5)) % h)
      g.fillRect(x, y, 1, 1)
    }
  }

  const rain = Math.round((rainAmount(weather.sky) * w) / 100)
  if (rain) {
    g.fillStyle = light.rain
    const slant = weather.sky === 'Storm' ? 70 : 30
    const len = weather.sky === 'Drizzle' ? 1 : weather.sky === 'Storm' ? 3 : 2
    for (let i = 0; i < rain; i++) {
      const seed = (i * 0.754877) % 1
      const x = Math.round((seed * w + t * slant) % w)
      const y = Math.round((((i * 0.5698) % 1) * h + t * (weather.sky === 'Drizzle' ? 50 : 90)) % h)
      g.fillRect(x, y, 1, len)
      if (weather.sky === 'Storm') g.fillRect(x + 1, y + len, 1, 1)
    }
  }

  if (snowy)
    for (let i = 0; i < Math.round(w * 0.35); i++) {
      const seed = (i * 0.754877) % 1
      const fall = 8 + seed * 10
      const y = Math.round((((i * 0.5698) % 1) * h + t * fall) % h)
      const x = Math.round((seed * w + Math.sin(t * 0.8 + i) * 3) % w)
      g.fillStyle = seed > 0.7 ? '#ffffff' : '#c8c4dc'
      g.fillRect(x, y, 1, 1)
    }

  // The roof we stand on, and what the weather leaves on it.
  g.fillStyle = light.roof
  g.fillRect(0, roof, w, h - roof)
  g.fillRect(0, roof - 9, w, 1)
  for (let x = 2; x < w; x += 9) g.fillRect(x, roof - 9, 1, 9)
  g.fillStyle = '#1a0d1c'
  g.fillRect(0, roof, w, 1)
  if (snowy) {
    g.fillStyle = light.snow
    g.fillRect(0, roof, w, 2)
    g.fillRect(0, roof - 10, w, 1)
    for (let x = 2; x < w; x += 9) g.fillRect(x, roof - 11, 1, 1)
  }
  if (wet) {
    // Puddles hold the neon, and shimmer.
    for (const p of city.puddles) {
      g.fillStyle = NEON[p.c]
      g.globalAlpha = 0.35 + 0.25 * Math.sin(t * 3 + p.x)
      for (let x = 0; x < p.w; x++) if ((x + Math.floor(t * 4)) % 3 !== 0) g.fillRect(p.x + x, p.y, 1, 1)
      g.globalAlpha = 1
    }
    // Drops falling from the rail, and splashes on the roof.
    g.fillStyle = light.rain
    for (let x = 2; x < w; x += 9) {
      const k = (t * 1.5 + hash1(x) * 3) % 1
      g.fillRect(x, roof - 8 + Math.round(k * 8), 1, 1)
    }
    g.fillStyle = '#6a6290'
    for (let i = 0; i < Math.round(w / 12); i++) {
      if ((t * 6 + i) % 1 > 0.4) continue
      const x = Math.floor(hash1(i + Math.floor(t * 2) * 31) * w)
      const y = roof + 2 + Math.floor(hash1(i * 3) * Math.max(1, h - roof - 3))
      g.fillRect(x - 1, y - 1, 1, 1)
      g.fillRect(x + 1, y - 1, 1, 1)
    }
  }
}

/** A city as a scene: it builds itself for a size and caches its sky. */
export function cityScene(kind: CityKind, w: number, h: number): Scene {
  const city = makeCity(w, h, kind === 'tokyo' ? 23 : 7, kind)
  let sky: HTMLCanvasElement | null = null
  let key = ''
  return {
    w,
    h,
    draw(g, time, calm, weather) {
      const overcast = weather.sky !== 'Clear'
      const k = `${weather.hour}:${overcast}`
      if (k !== key) {
        sky = document.createElement('canvas')
        sky.width = w
        sky.height = city.horizon
        drawSky(sky.getContext('2d')!, city, (kind === 'tokyo' ? TOKYO_LIGHTS : LIGHTS)[weather.hour], overcast)
        key = k
      }
      drawCity(g, city, sky!, time, calm, weather)
    },
  }
}
