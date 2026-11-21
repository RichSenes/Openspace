import type { Sky } from '../../../api/types'
import { rng } from '../body/shapes'
import { bayerAt, disc, ditherBands, fbm, hash1, LayerCache, valueNoise } from './pixels'
import type { Hour, Scene, Weather } from './types'

/**
 * Kyoto: mountains in layers, a temple with pagodas on the slopes, a path of vermilion gates, a
 * bamboo grove on one side and cherry trees in bloom on the other, stone lanterns and a veranda
 * in front. Sky lanterns rise after dusk. Everything that stands still is drawn once per hour and
 * weather; only lights, lanterns, petals and what falls move.
 */
export interface Kyoto {
  w: number
  h: number
  horizon: number
  floor: number
  ridges: number[][]
  pagodas: { x: number; base: number; w: number; floors: number }[]
  hall: { x: number; base: number; w: number }
  gates: { x: number; base: number; w: number; h: number }[]
  bamboo: { x: number; top: number; w: number; shade: number }[]
  blossoms: { x: number; y: number; r: number }[]
  stoneLanterns: { x: number; y: number }[]
  skyLanterns: { x: number; speed: number; seed: number }[]
}

interface Light {
  sky: string[]
  ridge: string[]
  wood: string
  deck: string
  roof: string
  plaster: string
  blossom: string[]
  bamboo: string[]
  lantern: number
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
    blossom: ['#3a2438', '#5a3450', '#7a4a68'],
    bamboo: ['#0a1410', '#10201a', '#183024'],
    lantern: 1,
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
    blossom: ['#8a4a6a', '#c87a96', '#f0a8bc'],
    bamboo: ['#16241c', '#22382a', '#305038'],
    lantern: 0.9,
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
    blossom: ['#a8708a', '#d8a0b4', '#f8cad8'],
    bamboo: ['#1e3428', '#2c4a36', '#3e6448'],
    lantern: 0.4,
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
    blossom: ['#c87a92', '#ecaac0', '#fcd8e4'],
    bamboo: ['#2a4a30', '#3e6a40', '#5a8a50'],
    lantern: 0,
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
  const ridges = [0, 1, 2].map((k) => Array.from({ length: w }, (_, x) => Math.round(horizon - h * (0.28 - k * 0.07) * fbm(noise, x / (90 - k * 20) + k * 10, k * 3, 4) - h * 0.04 * (2 - k))))
  // Pagodas on the slopes either side of the middle, standing on the nearest ridge.
  const pagodas = [
    { k: 0.7, w: 0.05, floors: 5 },
    { k: 0.84, w: 0.035, floors: 3 },
    { k: 0.24, w: 0.04, floors: 3 },
  ].map((p) => {
    const x = Math.round(w * p.k)
    return { x, base: Math.min(horizon + 4, ridges[2][Math.min(w - 1, x)] + 6), w: Math.max(10, Math.round(w * p.w)), floors: p.floors }
  })
  const hx = Math.round(w * 0.6)
  const hall = { x: hx, base: Math.min(floor - 10, ridges[2][Math.min(w - 1, hx)] + 16), w: Math.max(30, Math.round(w * 0.11)) }
  // A path of gates, receding toward the left slope: each smaller and higher than the last.
  const gates = Array.from({ length: 6 }, (_, i) => {
    const k = 1 - i * 0.14
    return { x: Math.round(w * (0.13 + i * 0.045)), base: Math.round(floor - 2 - i * (floor - horizon) * 0.12), w: Math.round(Math.max(10, w * 0.06) * k), h: Math.round((floor - horizon) * 0.7 * k) }
  })
  // Bamboo on the left edge, close and tall.
  const bamboo: Kyoto['bamboo'] = []
  for (let x = -2; x < w * 0.11; x += 3 + Math.floor(r() * 4)) bamboo.push({ x, top: Math.round(r() * h * 0.15) - 10, w: 2 + Math.floor(r() * 2), shade: r() })
  // Cherry trees in bloom on the right, crowns overlapping into a grove.
  const blossoms: Kyoto['blossoms'] = []
  for (let i = 0; i < Math.round(w / 14); i++) {
    const x = Math.round(w * (0.56 + r() * 0.46))
    blossoms.push({ x, y: Math.round(horizon + (floor - horizon) * (0.25 + r() * 0.6)), r: 6 + Math.round(r() * 8) })
  }
  blossoms.sort((a, b) => a.y - b.y)
  const stoneLanterns = [0.34, 0.66].map((k) => ({ x: Math.round(w * k), y: floor - 2 }))
  const skyLanterns = Array.from({ length: 22 }, () => ({ x: r(), speed: 0.4 + r() * 0.6, seed: r() }))
  return { w, h, horizon, floor, ridges, pagodas, hall, gates, bamboo, blossoms, stoneLanterns, skyLanterns }
}

/** A curved temple roof: wide eaves that lift at the ends. */
function roof(g: CanvasRenderingContext2D, x: number, y: number, w: number, colour: string, snow: string | null) {
  const half = w / 2
  for (let dx = -half; dx <= half; dx++) {
    const k = Math.abs(dx) / half
    const lift = Math.round(k ** 3 * 3)
    g.fillStyle = colour
    g.fillRect(Math.round(x + dx), y - lift, 1, Math.max(1, Math.round(3 * (1 - k * 0.5))))
    if (snow) {
      g.fillStyle = snow
      g.fillRect(Math.round(x + dx), y - lift - 1, 1, 1)
    }
  }
}

function pagoda(g: CanvasRenderingContext2D, p: Kyoto['pagodas'][number], L: Light, snow: string | null) {
  const storey = Math.max(4, Math.round(p.w * 0.5))
  for (let f = 0; f < p.floors; f++) {
    const y = p.base - f * storey
    const bw = Math.round(p.w * (1 - f * 0.09))
    g.fillStyle = L.wood
    g.fillRect(p.x - Math.round(bw * 0.3), y - storey + 3, Math.round(bw * 0.6), storey - 2)
    roof(g, p.x, y - storey + 3, bw, L.roof, snow)
  }
  g.fillStyle = '#a8882a'
  const top = p.base - p.floors * storey
  g.fillRect(p.x, top - 6, 1, 8)
}

function gate(g: CanvasRenderingContext2D, gt: Kyoto['gates'][number], L: Light, snow: string | null) {
  const red = L.lantern > 0.8 ? '#6a1a18' : '#c03a22'
  g.fillStyle = red
  g.fillRect(gt.x, gt.base - gt.h, 2, gt.h)
  g.fillRect(gt.x + gt.w - 2, gt.base - gt.h, 2, gt.h)
  g.fillRect(gt.x - 2, gt.base - gt.h + 3, gt.w + 4, 2)
  g.fillStyle = '#1a0a0a'
  for (let dx = -4; dx <= gt.w + 3; dx++) {
    const k = Math.abs(dx - gt.w / 2) / (gt.w / 2 + 4)
    g.fillRect(gt.x + dx, gt.base - gt.h - 1 - Math.round(k ** 3 * 2), 1, 2)
    if (snow) {
      g.fillStyle = snow
      g.fillRect(gt.x + dx, gt.base - gt.h - 2 - Math.round(k ** 3 * 2), 1, 1)
      g.fillStyle = '#1a0a0a'
    }
  }
}

/** Everything that stands still, drawn once per hour and weather. */
function drawStill(g: CanvasRenderingContext2D, k: Kyoto, L: Light, sky: Sky) {
  const snow = sky === 'Snow' ? L.snow : null
  const overcast = sky !== 'Clear'
  const img = g.createImageData(k.w, k.horizon)
  ditherBands(img, overcast ? L.sky.slice(1, 5) : L.sky, 1.3)
  g.putImageData(img, 0, 0)
  g.fillStyle = L.sky[L.sky.length - 1]
  g.fillRect(0, k.horizon, k.w, k.h - k.horizon)
  k.ridges.forEach((ridge, i) => {
    g.fillStyle = L.ridge[i]
    for (let x = 0; x < k.w; x++) g.fillRect(x, ridge[x], 1, k.floor - ridge[x])
    if (snow) {
      g.fillStyle = snow
      for (let x = 0; x < k.w; x++) if (ridge[x] < k.horizon - k.h * 0.12) g.fillRect(x, ridge[x], 1, i === 0 ? 2 : 1)
    }
    if (sky === 'Fog') {
      g.fillStyle = L.mist
      for (let x = 0; x < k.w; x++) for (let y = ridge[x]; y < ridge[x] + 10; y++) if (bayerAt(x, y) < 0.55 * (1 - (y - ridge[x]) / 10)) g.fillRect(x, y, 1, 1)
    }
  })
  for (const p of k.pagodas) pagoda(g, p, L, snow)
  // The main hall: a long low building under a great roof.
  const hl = k.hall
  g.fillStyle = L.wood
  g.fillRect(hl.x - Math.round(hl.w * 0.4), hl.base, Math.round(hl.w * 0.8), 2)
  g.fillStyle = L.plaster
  g.fillRect(hl.x - Math.round(hl.w * 0.36), hl.base - 9, Math.round(hl.w * 0.72), 9)
  g.fillStyle = L.wood
  for (let x = hl.x - Math.round(hl.w * 0.36); x < hl.x + hl.w * 0.36; x += 5) g.fillRect(x, hl.base - 9, 1, 9)
  roof(g, hl.x, hl.base - 9, hl.w, L.roof, snow)
  roof(g, hl.x, hl.base - 14, Math.round(hl.w * 0.55), L.roof, snow)
  for (let i = k.gates.length - 1; i >= 0; i--) gate(g, k.gates[i], L, snow)
  // Cherry trees: crowns of three tones, lit from the left, on dark trunks.
  for (const b of k.blossoms) {
    g.fillStyle = L.wood
    g.fillRect(b.x, b.y, 1, b.r + 2)
    g.fillRect(b.x - 2, b.y + 2, 2, 1)
    for (let dy = -b.r; dy <= b.r * 0.5; dy++)
      for (let dx = -b.r * 1.4; dx <= b.r * 1.4; dx++) {
        const d = (dx / (b.r * 1.4)) ** 2 + (dy / b.r) ** 2
        if (d > 1) continue
        const px = Math.round(b.x + dx)
        const py = Math.round(b.y + dy)
        const lit = -dx - dy * 0.8 + (hash1(px * 13 + py) - 0.5) * b.r * 0.8
        if (bayerAt(px, py) > 1 - d * 0.45) continue
        g.fillStyle = snow && dy < -b.r * 0.5 ? snow : L.blossom[lit > b.r * 0.5 ? 2 : lit > -b.r * 0.4 ? 1 : 0]
        g.fillRect(px, py, 1, 1)
      }
  }
  // Bamboo: tall stalks with their joints, a few leaves, close to us.
  for (const s of k.bamboo) {
    g.fillStyle = L.bamboo[s.shade > 0.6 ? 2 : s.shade > 0.3 ? 1 : 0]
    g.fillRect(s.x, s.top, s.w, k.floor - s.top)
    g.fillStyle = L.bamboo[0]
    for (let y = s.top + 6 + Math.floor(s.shade * 8); y < k.floor; y += 12) g.fillRect(s.x, y, s.w, 1)
    g.fillStyle = L.bamboo[2]
    for (let y = s.top + 10; y < k.floor * 0.7; y += 17) {
      const dir = (y >> 3) % 2 ? 1 : -1
      for (let i = 1; i <= 4; i++) g.fillRect(s.x + (dir > 0 ? s.w : -1) + dir * i, y + Math.round(i * 0.6), 1, 1)
    }
  }
  // Stone lanterns.
  for (const l of k.stoneLanterns) {
    g.fillStyle = '#4a4650'
    g.fillRect(l.x - 1, l.y - 10, 3, 10)
    g.fillRect(l.x - 3, l.y - 12, 7, 2)
    g.fillRect(l.x - 2, l.y - 16, 5, 4)
    g.fillRect(l.x - 4, l.y - 18, 9, 2)
    if (snow) {
      g.fillStyle = snow
      g.fillRect(l.x - 4, l.y - 19, 9, 1)
    }
  }
  // The veranda we stand on.
  g.fillStyle = L.deck
  g.fillRect(0, k.floor, k.w, k.h - k.floor)
  g.fillStyle = '#050303'
  for (let x = 0; x < k.w; x += 7) g.fillRect(x, k.floor + 1, 1, k.h - k.floor)
  g.fillRect(0, k.floor, k.w, 1)
  if (snow) {
    g.fillStyle = snow
    g.fillRect(0, k.floor - 1, k.w, 2)
  }
}

/** Where a sky lantern is at a time: it rises slowly, sways, and starts again below. */
export function skyLanternAt(l: Kyoto['skyLanterns'][number], k: Kyoto, t: number): { x: number; y: number } {
  const span = k.floor + 20
  const p = ((t * l.speed * 4 + l.seed * span) % span) / span
  return { x: Math.round(l.x * k.w + Math.sin(t * 0.4 + l.seed * 9) * 6), y: Math.round(k.floor - p * span) }
}

const still = new LayerCache()

/** One frame of Kyoto: the still layer, then the few things that move. */
export function drawKyoto(g: CanvasRenderingContext2D, k: Kyoto, time: number, calm: boolean, weather: Weather) {
  const t = calm ? 0 : time
  const L = KYOTO_LIGHTS[weather.hour]
  const sky = weather.sky
  g.drawImage(still.get('kyoto', `${weather.hour}:${sky}`, k.w, k.h, (c) => drawStill(c, k, L, sky)), 0, 0)

  if (L.stars && (sky === 'Clear' || sky === 'Cloudy'))
    for (let i = 0; i < k.w * 0.1; i++) {
      if (Math.sin(t * (1 + hash1(i + 9) * 2) + i) < -0.3) continue
      g.fillStyle = hash1(i + 3) > 0.8 ? '#ffffff' : '#7a80a8'
      g.fillRect(Math.floor(hash1(i) * k.w), Math.floor(hash1(i + 40) * k.horizon * 0.5), 1, 1)
    }
  if ((sky === 'Clear' || sky === 'Cloudy') && weather.hour !== 'day') {
    const night = weather.hour === 'night'
    g.fillStyle = night ? '#e8e4d0' : '#ffb070'
    disc(g, Math.round(k.w * (night ? 0.2 : 0.3)), Math.round(k.horizon * (night ? 0.22 : 0.55)), Math.max(4, Math.round(k.h * 0.035)))
  }

  // Lit windows on the pagodas and the stone lanterns after dark.
  if (L.lantern > 0.3) {
    for (const p of k.pagodas) {
      const storey = Math.max(4, Math.round(p.w * 0.5))
      for (let f = 0; f < p.floors; f++) {
        g.fillStyle = Math.sin(t * 0.7 + f + p.x) > -0.6 ? '#ffc070' : '#c08040'
        g.fillRect(p.x - 1, p.base - f * storey - storey + 5, 2, 2)
      }
    }
    for (const l of k.stoneLanterns) {
      const flick = calm ? 1 : 0.8 + 0.2 * Math.sin(t * 9 + l.x)
      g.fillStyle = `rgba(255, 190, 100, ${0.9 * L.lantern * flick})`
      g.fillRect(l.x - 1, l.y - 15, 3, 2)
    }
  }

  // Sky lanterns rising after dusk, in calm weather.
  if (L.lantern > 0.3 && (sky === 'Clear' || sky === 'Cloudy' || sky === 'Fog'))
    for (const l of k.skyLanterns) {
      const { x, y } = skyLanternAt(l, k, t)
      if (Math.abs(x - k.w / 2) < k.w * 0.14 && y < k.floor * 0.75) continue
      const flick = calm ? 1 : 0.85 + 0.15 * Math.sin(t * 6 + l.seed * 30)
      // Slower ones are farther: smaller and dimmer.
      const far = l.speed < 0.6
      const lw = far ? 2 : 4
      const lh = far ? 3 : 5
      g.fillStyle = `rgba(255, 150, 60, ${(far ? 0.15 : 0.28) * flick})`
      g.fillRect(x - 2, y - 2, lw + 4, lh + 4)
      g.fillStyle = far ? '#c8803a' : '#ffb860'
      g.fillRect(x, y, lw, lh)
      g.fillStyle = '#fff0c0'
      g.fillRect(x + Math.floor(lw / 2), y + lh - 1, 1, 1)
    }

  // What falls: petals in calm weather, snow, rain.
  if ((sky === 'Clear' || sky === 'Cloudy') && weather.hour !== 'night')
    for (let i = 0; i < 30; i++) {
      const s = hash1(i)
      const y = Math.round((hash1(i + 50) * k.h + t * (5 + s * 5)) % k.h)
      const x = Math.round((s * k.w + t * (5 + s * 5) + Math.sin(t * 1.3 + i) * 6) % k.w)
      g.fillStyle = L.blossom[1 + (i % 2)]
      g.fillRect(x, y, Math.sin(t * 3 + i) > 0 ? 2 : 1, 1)
    }
  if (sky === 'Snow')
    for (let i = 0; i < Math.round(k.w * 0.3); i++) {
      const s = hash1(i)
      g.fillStyle = s > 0.7 ? '#ffffff' : '#c8c8dc'
      g.fillRect(Math.round((s * k.w + Math.sin(t * 0.7 + i) * 3) % k.w), Math.round((hash1(i + 70) * k.h + t * (7 + s * 8)) % k.h), 1, 1)
    }
  const rain = sky === 'Storm' ? 60 : sky === 'Rain' ? 32 : sky === 'Drizzle' ? 12 : 0
  if (rain) {
    g.fillStyle = weather.hour === 'day' ? '#9ab0c4' : '#4a5078'
    for (let i = 0; i < Math.round((rain * k.w) / 100); i++) g.fillRect(Math.round((hash1(i) * k.w + t * 20) % k.w), Math.round((hash1(i + 30) * k.h + t * 90) % k.h), 1, sky === 'Drizzle' ? 1 : 2)
  }
  if (sky === 'Storm' && !calm && t % 9 < 0.1) {
    g.fillStyle = 'rgba(220, 220, 255, 0.35)'
    g.fillRect(0, 0, k.w, k.horizon)
  }
}

export function kyotoScene(w: number, h: number): Scene {
  const k = makeKyoto(w, h)
  return { w, h, draw: (g, time, calm, weather) => drawKyoto(g, k, time, calm, weather) }
}
