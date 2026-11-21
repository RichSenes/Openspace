import { rng } from '../body/shapes'
import { bayerAt, disc, fbm, hash1, hex, valueNoise } from './pixels'
import type { Hour, Scene, Weather } from './types'

/**
 * Space, seen from the window of a station: stars in three depths, a nebula, a spiral galaxy, a
 * banded giant with a ring and a moon, and a star whose light moves with the hour. The weather
 * becomes space weather: nebula, dust, meteors, ice, a solar storm with auroras.
 */
export interface Space {
  w: number
  h: number
  sill: number
  stars: { x: number; y: number; layer: number; k: number }[]
  galaxy: { x: number; y: number; r: number; arms: number }
  giant: { x: number; y: number; r: number }
  moon: { orbit: number; r: number }
  /** Planets far away, each its own kind. */
  planets: { x: number; y: number; r: number; kind: number; ring: boolean; tilt: number }[]
  /** Galaxies so far they are smudges. */
  smudges: { x: number; y: number; r: number; tilt: number }[]
  lights: { lat: number; lon: number }[]
}

export function makeSpace(w: number, h: number, seed = 41): Space {
  const r = rng(seed)
  const sill = Math.round(h * 0.9)
  // Mostly faint, far stars; a few near ones.
  const stars = Array.from({ length: Math.round(w * h * 0.014) }, () => {
    const k = r()
    return { x: r() * w, y: r() * sill, layer: k < 0.75 ? 0 : k < 0.95 ? 1 : 2, k: r() }
  })
  const giantR = Math.round(Math.min(w * 0.2, h * 0.28))
  // Planets keep clear of the middle, where Shiori is, and of each other.
  const planets: Space['planets'] = []
  for (let i = 0; i < 60 && planets.length < 7; i++) {
    const x = Math.round(r() * w)
    const y = Math.round(sill * (0.14 + r() * 0.7))
    const pr = 2 + Math.round(r() ** 2 * 6)
    const middle = Math.abs(x - w / 2) < w * 0.2 && y < sill * 0.75
    if (middle || planets.some((p) => Math.hypot(p.x - x, p.y - y) < 40)) continue
    planets.push({ x, y, r: pr, kind: Math.floor(r() * 5), ring: r() < 0.3, tilt: r() * 0.6 - 0.3 })
  }
  const smudges = Array.from({ length: 5 }, () => ({ x: Math.round(r() * w), y: Math.round(r() * sill * 0.8), r: 3 + Math.round(r() * 4), tilt: r() * Math.PI }))
  return {
    w,
    h,
    sill,
    stars,
    galaxy: { x: Math.round(w * 0.17), y: Math.round(h * 0.2), r: Math.round(Math.min(w, h) * 0.07), arms: 2 },
    giant: { x: Math.round(w * 0.9), y: Math.round(h * 0.86), r: giantR },
    moon: { orbit: giantR * 1.5, r: Math.max(2, Math.round(h * 0.018)) },
    planets,
    smudges,
    lights: Array.from({ length: 160 }, () => ({ lat: r() * 2 - 1, lon: r() })),
  }
}

/** Where the star stands, by hour: rising over the giant's limb, high, setting, or hidden. */
export function sunAt(s: Space, hour: Hour): { x: number; y: number; r: number } | null {
  if (hour === 'night') return null
  const r = Math.max(3, Math.round(s.h * 0.025))
  if (hour === 'dawn') return { x: Math.round(s.giant.x - s.giant.r * 0.55), y: Math.round(s.giant.y - s.giant.r * 0.86), r }
  if (hour === 'dusk') return { x: Math.round(s.w * 0.05), y: Math.round(s.h * 0.7), r: Math.round(r * 1.3) }
  return { x: Math.round(s.w * 0.55), y: Math.round(s.h * 0.12), r }
}

const PLANET_KINDS: [string, string, string][] = [
  ['#2a2228', '#5a4a48', '#8a7a6a'],
  ['#2a3a4a', '#6a9ab0', '#c8e8f0'],
  ['#3a2a1a', '#8a6a3a', '#d0a86a'],
  ['#0a1a3a', '#2a5a8a', '#6ab0d0'],
  ['#2a0a08', '#8a2a1a', '#ff7a3a'],
]

const NEBULA: Record<Hour, string[]> = {
  night: ['#02030a', '#0a0c26', '#1c1450', '#3a1a6a', '#6a2a80'],
  dawn: ['#03040c', '#0c1430', '#183058', '#3a4a7a', '#c07a6a'],
  day: ['#04050c', '#0e1630', '#1a3a5a', '#2a6a7a', '#e0b070'],
  dusk: ['#04030a', '#1a0a20', '#4a1438', '#8a2a3a', '#e07a3a'],
}

function nebula(s: Space, hour: Hour, density: number): HTMLCanvasElement {
  const c = document.createElement('canvas')
  c.width = s.w
  c.height = s.h
  const g = c.getContext('2d')!
  const img = g.createImageData(s.w, s.h)
  const cols = NEBULA[hour].map(hex)
  const n = valueNoise(7)
  const m = valueNoise(13)
  for (let y = 0; y < s.h; y++)
    for (let x = 0; x < s.w; x++) {
      // A band of gas across the sky, broken up by noise.
      const band = Math.exp(-(((y - s.h * 0.35 - (x - s.w * 0.5) * 0.25) / (s.h * 0.28)) ** 2))
      let v = fbm(n, x / 60, y / 60, 5) * band * density
      v += (fbm(m, x / 25, y / 25, 3) - 0.5) * 0.25 * band
      const f = Math.max(0, Math.min(0.999, v * 1.25)) * (cols.length - 1)
      const i = Math.floor(f)
      const c0 = cols[Math.min(cols.length - 1, f - i > bayerAt(x, y) ? i + 1 : i)]
      const o = (y * s.w + x) * 4
      img.data[o] = c0[0]
      img.data[o + 1] = c0[1]
      img.data[o + 2] = c0[2]
      img.data[o + 3] = 255
    }
  g.putImageData(img, 0, 0)
  return c
}

/** The giant: bands that drift, lit from the star's side, dark side with city lights. */
function giant(g: CanvasRenderingContext2D, s: Space, sun: { x: number; y: number } | null, t: number, aurora: number) {
  const { x: cx, y: cy, r } = s.giant
  const lx = sun ? (sun.x - cx) / r : -0.3
  const ly = sun ? (sun.y - cy) / r : -0.6
  const ll = Math.hypot(lx, ly, 0.8)
  const bands = ['#2a1a3a', '#4a2a4a', '#6a3a4a', '#8a5a4a', '#b08a6a', '#d0b08a']
  const n = valueNoise(3)
  for (let y = Math.max(0, cy - r); y < Math.min(s.h, cy + r); y++)
    for (let x = Math.max(0, cx - r); x < Math.min(s.w, cx + r); x++) {
      const dx = (x - cx) / r
      const dy = (y - cy) / r
      const d = dx * dx + dy * dy
      if (d > 1) continue
      const dz = Math.sqrt(1 - d)
      const lit = (dx * lx + dy * ly + dz * 0.8) / ll
      const lat = dy + 0.15 * Math.sin(dx * 3 + t * 0.05)
      const band = (Math.sin(lat * 18 + fbm(n, dx * 3 + t * 0.02, lat * 6, 3) * 3) + 1) / 2
      const shade = Math.max(0, lit) * (sun ? 1 : 0.25)
      const v = Math.min(0.999, band * 0.5 + shade * 0.6)
      if (shade < 0.08 && bayerAt(x, y) > 0.35) {
        g.fillStyle = '#05030a'
        g.fillRect(x, y, 1, 1)
        continue
      }
      const f = v * (bands.length - 1)
      const i = Math.floor(f)
      g.fillStyle = bands[Math.min(bands.length - 1, f - i > bayerAt(x, y) ? i + 1 : i)]
      g.fillRect(x, y, 1, 1)
    }
  // Cities on the night side.
  g.fillStyle = '#ffc070'
  for (const p of s.lights) {
    const ang = p.lon * Math.PI * 2 + t * 0.01
    const x = cx + Math.cos(ang) * Math.sqrt(1 - p.lat * p.lat) * r * 0.95
    const y = cy + p.lat * r * 0.95
    const lit = ((x - cx) / r) * lx + ((y - cy) / r) * ly
    if (Math.sin(ang) < 0 || lit > (sun ? -0.15 : 0.6) || y > s.sill) continue
    if (Math.sin(t * 2 + p.lon * 50) > -0.6) g.fillRect(Math.round(x), Math.round(y), 1, 1)
  }
  // The ring, in front of the planet below its middle and behind it above.
  for (let a = 0; a < Math.PI * 2; a += 0.004) {
    const rx = Math.cos(a) * r * 1.55
    const ry = Math.sin(a) * r * 0.22
    const x = Math.round(cx + rx * 0.96 - ry * 0.2)
    const y = Math.round(cy - r * 0.15 + ry + rx * 0.18)
    const behind = Math.sin(a) < 0 && (x - cx) ** 2 + (y - cy) ** 2 < r * r
    if (behind || y >= s.sill || y < 0) continue
    g.fillStyle = Math.floor(a * 40) % 5 === 0 ? '#6a5a6a' : '#b0a090'
    g.fillRect(x, y, 1, 1)
  }
  // A solar storm lights auroras on the limb.
  if (aurora > 0)
    for (let a = -Math.PI; a < 0; a += 0.01) {
      const x = Math.round(cx + Math.cos(a) * r * 1.02)
      const base = cy + Math.sin(a) * r * 1.02
      const len = Math.round((4 + 8 * (0.5 + 0.5 * Math.sin(a * 20 + t * 3))) * aurora)
      for (let k = 0; k < len; k++) {
        if (bayerAt(x, Math.round(base) - k) > 1 - k / len) continue
        g.fillStyle = k < len / 2 ? '#6dff9a' : '#ff6fd0'
        g.fillRect(x, Math.round(base) - k, 1, 1)
      }
    }
}

function galaxy(g: CanvasRenderingContext2D, s: Space, t: number, dim: boolean) {
  const { x, y, r, arms } = s.galaxy
  const spin = t * 0.01
  // A soft core and disc, then the arms on top.
  g.fillStyle = '#3a2a6a'
  for (let dy = -r; dy <= r; dy++)
    for (let dx = -r; dx <= r; dx++) {
      const d = Math.hypot(dx, dy / 0.45) / r
      if (d < 1 && bayerAt(x + dx, y + dy) < (1 - d) ** 2 * 0.8) g.fillRect(x + dx, y + dy, 1, 1)
    }
  for (let i = 0; i < 2600; i++) {
    const k = hash1(i)
    const arm = i % arms
    const dist = k ** 0.7 * r
    const spread = (hash1(i + 7) - 0.5) * (0.25 + 0.6 * (1 - k))
    const ang = (arm / arms) * Math.PI * 2 + dist * (2.6 / r) * Math.PI + spin + spread
    const px = Math.round(x + Math.cos(ang) * dist)
    const py = Math.round(y + Math.sin(ang) * dist * 0.45)
    if (dim && hash1(i + 3) > 0.45) continue
    g.fillStyle = dist < r * 0.15 ? '#fff0d8' : hash1(i + 11) > 0.75 ? '#9ab8ff' : hash1(i + 13) > 0.5 ? '#c8a8ff' : '#7a6ab8'
    g.fillRect(px, py, 1, 1)
  }
  g.fillStyle = '#ffe8c8'
  disc(g, x, y, 2, 3)
  g.fillStyle = '#ffffff'
  disc(g, x, y, 1)
}

/** One frame of space; `nebulaImage` is drawn once per hour by the scene. */
export function drawSpace(g: CanvasRenderingContext2D, s: Space, neb: HTMLCanvasElement, time: number, calm: boolean, weather: Weather, cache: { giant?: HTMLCanvasElement; key?: string } = {}) {
  const t = calm ? 0 : time
  g.drawImage(neb, 0, 0)
  const sky = weather.sky

  // Stars drift slowly, the nearer the faster.
  for (const st of s.stars) {
    const speed = [0.3, 0.8, 1.8][st.layer]
    const x = Math.round((((st.x - t * speed) % s.w) + s.w) % s.w)
    const tw = Math.sin(t * (1 + st.k * 3) + st.k * 50)
    if (st.layer === 0 && tw < -0.3) continue
    g.fillStyle = st.layer === 2 && tw > 0.7 ? '#ffffff' : st.k > 0.8 ? '#ffd8a8' : st.k > 0.6 ? '#a8c8ff' : '#8a88b0'
    g.fillRect(x, Math.round(st.y), 1, 1)
    if (st.layer === 2 && st.k > 0.93) {
      g.fillRect(x - 1, Math.round(st.y), 1, 1)
      g.fillRect(x + 1, Math.round(st.y), 1, 1)
      g.fillRect(x, Math.round(st.y) - 1, 1, 1)
      g.fillRect(x, Math.round(st.y) + 1, 1, 1)
    }
  }

  galaxy(g, s, t, sky === 'Cloudy' || sky === 'Fog')

  // Galaxies so far away they are smudges.
  for (const m of s.smudges) {
    g.fillStyle = '#6a5a8a'
    for (let dy = -m.r; dy <= m.r; dy++)
      for (let dx = -m.r * 2; dx <= m.r * 2; dx++) {
        const rx = dx * Math.cos(m.tilt) + dy * Math.sin(m.tilt)
        const ry = -dx * Math.sin(m.tilt) + dy * Math.cos(m.tilt)
        const d = Math.hypot(rx / 2, ry * 1.4) / m.r
        if (d < 1 && bayerAt(m.x + dx, m.y + dy) < (1 - d) * 0.7) g.fillRect(m.x + dx, m.y + dy, 1, 1)
      }
    g.fillStyle = '#d8c8ff'
    g.fillRect(m.x, m.y, 1, 1)
  }

  // Distant planets: rocky, ice, banded, ocean, lava; some ringed.
  for (const p of s.planets) {
    const [dark, mid, light] = PLANET_KINDS[p.kind]
    for (let dy = -p.r; dy <= p.r; dy++)
      for (let dx = -p.r; dx <= p.r; dx++) {
        const d = dx * dx + dy * dy
        if (d > p.r * p.r) continue
        const lit = (-dx - dy * 0.5) / p.r
        const band = p.kind === 2 && Math.abs((dy + p.r) % 3) < 1
        g.fillStyle = lit > 0.35 ? light : lit > -0.3 ? (band ? dark : mid) : dark
        g.fillRect(p.x + dx, p.y + dy, 1, 1)
      }
    if (p.ring) {
      g.fillStyle = '#a89a8a'
      for (let x = -p.r * 2; x <= p.r * 2; x++) {
        const y = Math.round(x * p.tilt)
        if (Math.abs(x) > p.r || y > 0) g.fillRect(p.x + x, p.y + y, 1, 1)
      }
    }
  }

  const sun = sunAt(s, weather.hour)
  if (sun) {
    g.fillStyle = weather.hour === 'day' ? '#fff8e0' : '#ffb070'
    disc(g, sun.x, sun.y, sun.r * 1.3, sun.r * 1.6)
    g.fillStyle = '#ffffff'
    disc(g, sun.x, sun.y, sun.r)
    // A thin flare across the window.
    g.fillStyle = '#fff0c8'
    for (let x = -sun.r * 6; x <= sun.r * 6; x++) if (bayerAt(sun.x + x, sun.y) < 1 - Math.abs(x) / (sun.r * 6)) g.fillRect(sun.x + x, sun.y, 1, 1)
  }

  // The giant turns slowly; redrawn each frame so its bands glide instead of jumping.
  const storm = sky === 'Storm' ? 1 : 0
  const gk = `${weather.hour}:${storm}:${t}`
  if (cache.key !== gk || !cache.giant) {
    const c = cache.giant ?? document.createElement('canvas')
    c.width = s.w
    c.height = s.h
    const gg = c.getContext('2d')!
    gg.clearRect(0, 0, s.w, s.h)
    giant(gg, s, sun, t, storm)
    cache.giant = c
    cache.key = gk
  }
  g.drawImage(cache.giant, 0, 0)

  // The moon, on its orbit around the giant.
  const ang = -2.2 + Math.sin(t * 0.02) * 0.3
  const mx = Math.round(s.giant.x + Math.cos(ang) * s.moon.orbit)
  const my = Math.round(s.giant.y + Math.sin(ang) * s.moon.orbit * 0.5)
  g.fillStyle = '#5a5470'
  disc(g, mx, my, s.moon.r)
  g.fillStyle = sun ? '#c8c0d8' : '#6a6480'
  disc(g, mx - 1, my - 1, Math.max(1, s.moon.r - 2))

  if (sky === 'Fog') {
    // Dust: dark lanes drifting across.
    g.fillStyle = '#1a1018'
    for (let y = 0; y < s.sill; y++) {
      const lane = Math.sin(y * 0.06 + t * 0.1) * Math.sin(y * 0.017)
      if (lane < 0.3) continue
      for (let x = 0; x < s.w; x++) if (bayerAt(x + Math.round(t * 2), y) < (lane - 0.3) * 1.2) g.fillRect(x, y, 1, 1)
    }
  }

  const meteors = sky === 'Rain' ? 14 : sky === 'Drizzle' ? 4 : sky === 'Storm' ? 8 : 0
  for (let i = 0; i < meteors; i++) {
    const period = 2 + hash1(i) * 3
    const p = ((t + hash1(i + 5) * period) % period) / period
    if (p > 0.5) continue
    const x0 = hash1(i + 9) * s.w * 1.2
    const y0 = hash1(i + 19) * s.sill * 0.5
    const len = 6 + Math.round(hash1(i + 29) * 10)
    for (let k = 0; k < len; k++) {
      const x = Math.round(x0 - p * s.w * 0.8 - k)
      const y = Math.round(y0 + p * s.sill * 0.5 + k * 0.5)
      if (bayerAt(x, y) > 1 - k / len) continue
      g.fillStyle = k === 0 ? '#ffffff' : k < 3 ? '#ffe0a0' : '#c07a4a'
      g.fillRect(x, y, 1, 1)
    }
  }

  if (sky === 'Snow')
    for (let i = 0; i < Math.round(s.w * 0.2); i++) {
      const x = Math.round((hash1(i) * s.w + Math.sin(t * 0.3 + i) * 6) % s.w)
      const y = Math.round((hash1(i + 70) * s.sill + t * (2 + hash1(i + 3) * 3)) % s.sill)
      g.fillStyle = Math.sin(t * 4 + i) > 0.8 ? '#ffffff' : '#7a9ab8'
      g.fillRect(x, y, 1, 1)
    }

  if (sky === 'Storm' && !calm && Math.sin(t * 9) > 0.97) {
    g.fillStyle = 'rgba(180, 255, 210, 0.12)'
    g.fillRect(0, 0, s.w, s.sill)
  }

  // The station: a sill, two struts and a console that blinks.
  g.fillStyle = '#07060c'
  g.fillRect(0, s.sill, s.w, s.h - s.sill)
  g.fillRect(0, 0, 3, s.sill)
  g.fillRect(s.w - 3, 0, 3, s.sill)
  g.fillStyle = '#1c1a2a'
  g.fillRect(0, s.sill, s.w, 1)
  g.fillRect(3, 0, 1, s.sill)
  g.fillRect(s.w - 4, 0, 1, s.sill)
  for (let x = 8; x < s.w - 8; x += 14) {
    const on = Math.sin(t * (1 + (x % 5)) + x) > 0
    g.fillStyle = on ? (x % 3 === 0 ? '#ff6a3a' : '#3de0ff') : '#1a2030'
    g.fillRect(x, s.sill + 3, 2, 1)
  }
}

export function spaceScene(w: number, h: number): Scene {
  const s = makeSpace(w, h)
  let neb: HTMLCanvasElement | null = null
  let key = ''
  const cache = {}
  return {
    w,
    h,
    draw(g, time, calm, weather) {
      const density = weather.sky === 'Cloudy' ? 1.25 : weather.sky === 'Fog' ? 1.1 : 0.85
      const k = `${weather.hour}:${density}`
      if (k !== key) {
        neb = nebula(s, weather.hour, density)
        key = k
      }
      drawSpace(g, s, neb!, time, calm, weather, cache)
    },
  }
}
