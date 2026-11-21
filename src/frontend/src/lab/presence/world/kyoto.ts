import type { Sky } from '../../../api/types'
import { rng } from '../body/shapes'
import { bayerAt, disc, ditherBands, fbm, hash1, valueNoise } from './pixels'
import type { Hour, Scene, Weather } from './types'

/**
 * Kyoto, the calm counterpoint: mountains in layers, a five-storey pagoda on the hill, temple
 * roofs among maples, a vermilion gate, stone lanterns lit at dusk, and a wooden veranda in front.
 * The weather shows in the details: falling maple leaves when clear, mist in the valleys, rain
 * rings on the pond, snow on every roof.
 */
export interface Roof {
  x: number
  y: number
  w: number
  /** Floors stacked (the pagoda has five). */
  floors: number
}

export interface Kyoto {
  w: number
  h: number
  horizon: number
  floor: number
  ridges: number[][]
  pagoda: { x: number; base: number; w: number }
  roofs: Roof[]
  trees: { x: number; y: number; r: number; tone: number }[]
  lanterns: { x: number; y: number }[]
  gate: { x: number; y: number; w: number; h: number }
  pond: { x: number; y: number; w: number; h: number }
}

interface Light {
  sky: string[]
  ridge: string[]
  wood: string
  /** The veranda's boards, darker than the temples' wood. */
  deck: string
  roof: string
  plaster: string
  maple: string[]
  lantern: number
  water: string
  mist: string
  snow: string
  stars: boolean
}

export const KYOTO_LIGHTS: Record<Hour, Light> = {
  night: {
    sky: ['#05060f', '#0a0c1e', '#11152e', '#1a1d3e', '#262650', '#33305c'],
    ridge: ['#0c0e1e', '#10132a', '#151a36'],
    wood: '#1a0e10',
    deck: '#0c0708',
    roof: '#0e1016',
    plaster: '#3a3644',
    maple: ['#2a0c14', '#40121c', '#5a1a22'],
    lantern: 1,
    water: '#0e1430',
    mist: '#2a2c48',
    snow: '#8a90b0',
    stars: true,
  },
  dusk: {
    sky: ['#1a1238', '#3a1c4e', '#6e2c56', '#b04a50', '#e0784a', '#f8b060'],
    ridge: ['#3a2448', '#2c1c3c', '#201630'],
    wood: '#2a1014',
    deck: '#140a0c',
    roof: '#1a141e',
    plaster: '#8a6a6a',
    maple: ['#6a1a1e', '#a82a22', '#e04a28'],
    lantern: 0.9,
    water: '#3a2a4a',
    mist: '#8a6070',
    snow: '#d8c0c8',
    stars: false,
  },
  dawn: {
    sky: ['#1c2448', '#36406a', '#62648a', '#a08498', '#e0a49a', '#f8d0a8'],
    ridge: ['#4a4c70', '#3a3c5e', '#2c2e4c'],
    wood: '#3a1a18',
    deck: '#1a0f10',
    roof: '#262838',
    plaster: '#b0a8b0',
    maple: ['#7a2028', '#b03a30', '#e0603a'],
    lantern: 0.4,
    water: '#5a6488',
    mist: '#c0b0c0',
    snow: '#e0e0f0',
    stars: false,
  },
  day: {
    sky: ['#5a88b8', '#6e98c4', '#88aed0', '#a4c2d8', '#c0d4de', '#d8e2e0'],
    ridge: ['#7e96a8', '#5e7a8a', '#44606c'],
    wood: '#8a2a1e',
    deck: '#2a1a16',
    roof: '#3a3e48',
    plaster: '#e8e0d4',
    maple: ['#8a2018', '#c83a20', '#f06a2a'],
    lantern: 0,
    water: '#5a8aa8',
    mist: '#d0dce0',
    snow: '#f4f4f8',
    stars: false,
  },
}

export function makeKyoto(w: number, h: number, seed = 81): Kyoto {
  const r = rng(seed)
  const horizon = Math.round(h * 0.62)
  const floor = Math.round(h * 0.88)
  const noise = valueNoise(seed)
  // Three ridges, far to near, each a line of heights.
  const ridges = [0, 1, 2].map((k) => Array.from({ length: w }, (_, x) => Math.round(horizon - h * (0.28 - k * 0.07) * fbm(noise, x / (90 - k * 20) + k * 10, k * 3, 4) - h * 0.04 * (2 - k))))
  const pagoda = { x: Math.round(w * 0.78), base: Math.round(horizon + h * 0.02), w: Math.max(14, Math.round(w * 0.05)) }
  const roofs: Roof[] = []
  for (let i = 0; i < 6; i++) {
    const x = Math.round(r() * w)
    if (Math.abs(x - w / 2) < w * 0.18 || Math.abs(x - pagoda.x) < pagoda.w * 2) continue
    roofs.push({ x, y: Math.round(horizon + r() * (floor - horizon) * 0.35), w: 22 + Math.floor(r() * 26), floors: 1 + (r() < 0.3 ? 1 : 0) })
  }
  const trees = Array.from({ length: Math.round(w / 9) }, () => ({ x: Math.round(r() * w), y: Math.round(horizon + r() * (floor - horizon) * 0.7), r: 4 + Math.round(r() * 7), tone: r() }))
  trees.sort((a, b) => a.y - b.y)
  const gate = { x: Math.round(w * 0.15), y: floor - 4, w: Math.max(22, Math.round(w * 0.07)), h: Math.round((floor - horizon) * 0.9) }
  const lanterns = [0.32, 0.4, 0.6, 0.68].map((k) => ({ x: Math.round(w * k), y: floor - 2 }))
  const pond = { x: Math.round(w * 0.5), y: Math.round(floor - (floor - horizon) * 0.22), w: Math.round(w * 0.22), h: Math.max(4, Math.round((floor - horizon) * 0.12)) }
  return { w, h, horizon, floor, ridges, pagoda, roofs, trees, lanterns, gate, pond }
}

/** A curved temple roof: wide eaves that lift at the ends. */
function roof(g: CanvasRenderingContext2D, x: number, y: number, w: number, colour: string, snow: string | null) {
  g.fillStyle = colour
  const half = w / 2
  for (let dx = -half; dx <= half; dx++) {
    const k = Math.abs(dx) / half
    const lift = Math.round(k ** 3 * 3)
    const thick = Math.max(1, Math.round(3 * (1 - k * 0.5)))
    g.fillRect(Math.round(x + dx), y - lift, 1, thick)
    if (snow) {
      g.fillStyle = snow
      g.fillRect(Math.round(x + dx), y - lift - 1, 1, 1)
      g.fillStyle = colour
    }
  }
}

function pagoda(g: CanvasRenderingContext2D, k: Kyoto, L: Light, t: number, snowy: boolean) {
  const { x, base, w } = k.pagoda
  const storey = Math.round(w * 0.5)
  for (let f = 0; f < 5; f++) {
    const y = base - f * storey
    const bw = Math.round(w * (1 - f * 0.1))
    g.fillStyle = L.wood
    g.fillRect(x - Math.round(bw * 0.3), y - storey + 3, Math.round(bw * 0.6), storey - 2)
    // A lit window on each storey after dark.
    if (L.lantern > 0.3) {
      g.fillStyle = Math.sin(t * 0.7 + f) > -0.6 ? '#ffc070' : '#c08040'
      g.fillRect(x - 1, y - storey + 5, 2, 2)
    }
    roof(g, x, y - storey + 3, bw, L.roof, snowy ? L.snow : null)
  }
  g.fillStyle = '#a8882a'
  g.fillRect(x, base - 5 * storey - 6, 1, 8)
  for (let i = 0; i < 4; i++) g.fillRect(x - 1, base - 5 * storey - 5 + i * 2, 3, 1)
}

function maple(g: CanvasRenderingContext2D, tr: Kyoto['trees'][number], L: Light, t: number, snowy: boolean) {
  // A crown of dithered colour on a dark trunk; it sways a little in the wind.
  g.fillStyle = L.wood
  g.fillRect(tr.x, tr.y, 1, tr.r + 2)
  const sway = Math.round(Math.sin(t * 0.6 + tr.x) * 0.6)
  for (let dy = -tr.r; dy <= tr.r * 0.4; dy++)
    for (let dx = -tr.r * 1.3; dx <= tr.r * 1.3; dx++) {
      const d = (dx / (tr.r * 1.3)) ** 2 + (dy / tr.r) ** 2
      if (d > 1) continue
      const px = Math.round(tr.x + dx + sway * (1 - (dy + tr.r) / (tr.r * 1.4)))
      const py = Math.round(tr.y + dy)
      const lit = -dx - dy * 0.7 + (hash1(px * 13 + py) - 0.5) * tr.r
      g.fillStyle = snowy && dy < -tr.r * 0.55 && lit > -tr.r * 0.2 ? L.snow : L.maple[lit > tr.r * 0.5 ? 2 : lit > -tr.r * 0.3 ? 1 : 0]
      if (bayerAt(px, py) < 1 - d * 0.5) g.fillRect(px, py, 1, 1)
    }
}

function gate(g: CanvasRenderingContext2D, k: Kyoto, L: Light) {
  const { x, y, w, h } = k.gate
  const red = L.lantern > 0.8 ? '#5a1414' : '#c03a22'
  g.fillStyle = red
  g.fillRect(x, y - h, 2, h)
  g.fillRect(x + w - 2, y - h, 2, h)
  g.fillRect(x - 3, y - h + 3, w + 6, 2)
  g.fillStyle = '#1a0a0a'
  for (let dx = -5; dx <= w + 4; dx++) {
    const k2 = Math.abs(dx - w / 2) / (w / 2 + 5)
    g.fillRect(x + dx, y - h - 1 - Math.round(k2 ** 3 * 3), 1, 2)
  }
  g.fillStyle = red
  g.fillRect(x + Math.round(w / 2) - 2, y - h + 1, 4, 3)
}

function lantern(g: CanvasRenderingContext2D, l: { x: number; y: number }, L: Light, t: number, calm: boolean, snowy: boolean) {
  g.fillStyle = '#4a4650'
  g.fillRect(l.x - 1, l.y - 10, 3, 10)
  g.fillRect(l.x - 3, l.y - 12, 7, 2)
  g.fillRect(l.x - 2, l.y - 16, 5, 4)
  g.fillRect(l.x - 4, l.y - 18, 9, 2)
  if (snowy) {
    g.fillStyle = L.snow
    g.fillRect(l.x - 4, l.y - 19, 9, 1)
  }
  if (L.lantern > 0) {
    const flick = calm ? 1 : 0.8 + 0.2 * Math.sin(t * 9 + l.x)
    g.fillStyle = `rgba(255, 190, 100, ${0.9 * L.lantern * flick})`
    g.fillRect(l.x - 1, l.y - 15, 3, 2)
    g.fillStyle = `rgba(255, 170, 80, ${0.18 * L.lantern})`
    disc(g, l.x, l.y - 14, 5)
  }
}

/** One frame of Kyoto over its sky and mountains, drawn once per hour by the scene. */
export function drawKyoto(g: CanvasRenderingContext2D, k: Kyoto, back: HTMLCanvasElement, time: number, calm: boolean, weather: Weather) {
  const t = calm ? 0 : time
  const L = KYOTO_LIGHTS[weather.hour]
  const sky = weather.sky
  const snowy = sky === 'Snow'
  const wet = sky === 'Rain' || sky === 'Drizzle' || sky === 'Storm'
  g.drawImage(back, 0, 0)

  if (L.stars && (sky === 'Clear' || sky === 'Cloudy'))
    for (let i = 0; i < k.w * 0.1; i++) {
      const x = Math.floor(hash1(i) * k.w)
      const y = Math.floor(hash1(i + 40) * k.horizon * 0.5)
      if (Math.sin(t * (1 + hash1(i + 9) * 2) + i) > -0.3) {
        g.fillStyle = hash1(i + 3) > 0.8 ? '#ffffff' : '#7a80a8'
        g.fillRect(x, y, 1, 1)
      }
    }
  if ((sky === 'Clear' || sky === 'Cloudy') && weather.hour !== 'day') {
    const night = weather.hour === 'night'
    g.fillStyle = night ? '#e8e4d0' : '#ffb070'
    disc(g, Math.round(k.w * (night ? 0.2 : 0.3)), Math.round(k.horizon * (night ? 0.22 : 0.55)), Math.max(4, Math.round(k.h * 0.035)), 3)
  }

  // The ridges, far to near.
  k.ridges.forEach((ridge, i) => {
    g.fillStyle = L.ridge[i]
    for (let x = 0; x < k.w; x++) g.fillRect(x, ridge[x], 1, k.floor - ridge[x])
    if (snowy) {
      g.fillStyle = L.snow
      for (let x = 0; x < k.w; x++) if (ridge[x] < k.horizon - k.h * 0.12) g.fillRect(x, ridge[x], 1, 1 + (i === 0 ? 1 : 0))
    }
    // Mist in the valleys between the ridges.
    if (sky === 'Fog' || weather.hour === 'dawn') {
      g.fillStyle = L.mist
      const drift = t * 2
      for (let x = 0; x < k.w; x++) {
        const top = ridge[x]
        for (let y = top; y < top + 8; y++) if (bayerAt(x + Math.round(drift), y) < 0.5 * (1 - (y - top) / 8) * (0.6 + 0.4 * Math.sin((x + drift) * 0.05 + i))) g.fillRect(x, y, 1, 1)
      }
    }
  })

  pagoda(g, k, L, t, snowy)
  for (const rf of k.roofs) {
    g.fillStyle = L.plaster
    g.fillRect(rf.x - Math.round(rf.w * 0.35), rf.y, Math.round(rf.w * 0.7), 6)
    g.fillStyle = L.wood
    for (let x = rf.x - Math.round(rf.w * 0.35); x < rf.x + rf.w * 0.35; x += 4) g.fillRect(x, rf.y, 1, 6)
    roof(g, rf.x, rf.y, rf.w, L.roof, snowy ? L.snow : null)
    if (rf.floors > 1) roof(g, rf.x, rf.y - 5, Math.round(rf.w * 0.6), L.roof, snowy ? L.snow : null)
  }
  for (const tr of k.trees) maple(g, tr, L, t, snowy)

  // The pond: holds the sky, ripples in the wind, rings in the rain.
  const p = k.pond
  for (let y = 0; y < p.h; y++) {
    const half = Math.round((p.w / 2) * Math.sqrt(1 - ((y - p.h / 2) / (p.h / 2)) ** 2))
    g.fillStyle = L.water
    g.fillRect(p.x - half, p.y + y, half * 2, 1)
    g.fillStyle = L.sky[L.sky.length - 2]
    for (let x = -half; x < half; x += 5) if (Math.sin(t * 1.5 + x * 0.4 + y) > 0.6) g.fillRect(p.x + x, p.y + y, 2, 1)
  }
  if (wet && !calm)
    for (let i = 0; i < (sky === 'Drizzle' ? 3 : 8); i++) {
      const age = (t * 1.2 + hash1(i)) % 1
      const cx = p.x + Math.round((hash1(i + Math.floor(t * 1.2 + hash1(i))) - 0.5) * p.w * 0.8)
      const cy = p.y + Math.round(hash1(i * 7) * (p.h - 2)) + 1
      const rr = Math.round(age * 4)
      g.fillStyle = '#c8d4f0'
      g.fillRect(cx - rr, cy, 1, 1)
      g.fillRect(cx + rr, cy, 1, 1)
    }

  gate(g, k, L)
  for (const l of k.lanterns) lantern(g, l, L, t, calm, snowy)

  // The veranda we stand on.
  g.fillStyle = L.deck
  g.fillRect(0, k.floor, k.w, k.h - k.floor)
  g.fillStyle = '#050303'
  for (let x = 0; x < k.w; x += 7) g.fillRect(x, k.floor + 1, 1, k.h - k.floor)
  g.fillRect(0, k.floor, k.w, 1)
  if (snowy) {
    g.fillStyle = L.snow
    g.fillRect(0, k.floor - 1, k.w, 2)
  }

  // What falls: maple leaves on clear and cloudy days, petals of snow, rain.
  if ((sky === 'Clear' || sky === 'Cloudy') && weather.hour !== 'night')
    for (let i = 0; i < 26; i++) {
      const s = hash1(i)
      const y = Math.round((hash1(i + 50) * k.h + t * (6 + s * 6)) % k.h)
      const x = Math.round((s * k.w + t * (4 + s * 5) + Math.sin(t * 1.3 + i) * 6) % k.w)
      g.fillStyle = L.maple[1 + (i % 2)]
      g.fillRect(x, y, Math.sin(t * 3 + i) > 0 ? 2 : 1, 1)
    }
  if (snowy)
    for (let i = 0; i < Math.round(k.w * 0.35); i++) {
      const s = hash1(i)
      const y = Math.round((hash1(i + 70) * k.h + t * (7 + s * 8)) % k.h)
      const x = Math.round((s * k.w + Math.sin(t * 0.7 + i) * 3) % k.w)
      g.fillStyle = s > 0.7 ? '#ffffff' : '#c8c8dc'
      g.fillRect(x, y, 1, 1)
    }
  const rain = sky === 'Storm' ? 60 : sky === 'Rain' ? 32 : sky === 'Drizzle' ? 12 : 0
  if (rain) {
    g.fillStyle = weather.hour === 'day' ? '#9ab0c4' : '#4a5078'
    for (let i = 0; i < Math.round((rain * k.w) / 100); i++) {
      const s = hash1(i)
      const x = Math.round((s * k.w + t * 20) % k.w)
      const y = Math.round((hash1(i + 30) * k.h + t * 90) % k.h)
      g.fillRect(x, y, 1, sky === 'Drizzle' ? 1 : 2)
    }
  }
  if (sky === 'Storm' && !calm && t % 9 < 0.1) {
    g.fillStyle = 'rgba(220, 220, 255, 0.35)'
    g.fillRect(0, 0, k.w, k.horizon)
  }
}

/** Kyoto as a scene: sky and clouds drawn once per hour and weather. */
export function kyotoScene(w: number, h: number): Scene {
  const k = makeKyoto(w, h)
  let back: HTMLCanvasElement | null = null
  let key = ''
  return {
    w,
    h,
    draw(g, time, calm, weather) {
      const overcast = (['Cloudy', 'Fog', 'Drizzle', 'Rain', 'Snow', 'Storm'] as Sky[]).includes(weather.sky)
      const kk = `${weather.hour}:${overcast}`
      if (kk !== key) {
        back = document.createElement('canvas')
        back.width = w
        back.height = h
        const bg = back.getContext('2d')!
        const img = bg.createImageData(w, k.horizon)
        const L = KYOTO_LIGHTS[weather.hour]
        ditherBands(img, overcast ? L.sky.slice(1, 5) : L.sky, 1.3)
        bg.putImageData(img, 0, 0)
        bg.fillStyle = L.sky[L.sky.length - 1]
        bg.fillRect(0, k.horizon, w, h - k.horizon)
        key = kk
      }
      drawKyoto(g, k, back!, time, calm, weather)
    },
  }
}
