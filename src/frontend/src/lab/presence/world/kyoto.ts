import type { Sky } from '../../../api/types'
import { rng } from '../body/shapes'
import { bayerAt, disc, ditherBands, fbm, hash1, LayerCache, valueNoise, wavesAcross } from './pixels'
import type { Hour, Scene, Weather } from './types'

/**
 * Kyoto seen from a temple veranda on the hill, far above the town: snowy peaks, wooded ridges
 * with a bamboo grove and a path of gates climbing them, pagodas, a temple hall, a town of tiled
 * roofs with lit windows and street lanterns, cherry trees in bloom among the roofs. Everything
 * small, everything far. What stands still is drawn once per hour and weather; what moves is the
 * life of the place: lights, smoke from incense, sky lanterns, birds, petals, the weather.
 */
export interface House {
  x: number
  w: number
  h: number
  /** Lit windows: x in the house, when it lights. */
  windows: { x: number; k: number }[]
  /** The house sits a little above or below its terrace line, so rows never look ruled. */
  dy: number
}

export interface Kyoto {
  w: number
  h: number
  horizon: number
  floor: number
  ridges: number[][]
  /** Terraces of houses climbing the hill, back to front: each a base line and its houses. */
  terraces: { base: number; houses: House[] }[]
  pagodas: { x: number; base: number; w: number; floors: number }[]
  hall: { x: number; base: number; w: number }
  gates: { x: number; y: number }[]
  grove: { x0: number; x1: number }
  blossoms: { x: number; y: number; r: number }[]
  streetLights: { x: number; y: number; seed: number }[]
  skyLanterns: { x: number; speed: number; seed: number; far: boolean }[]
}

interface Light {
  sky: string[]
  ridge: string[]
  peak: string
  roof: string
  roofLit: string
  wall: string
  wood: string
  /** The pagodas' painted wood. */
  pagoda: string
  blossom: string[]
  bamboo: string[]
  lit: number
  lantern: number
  cloud: string
  mist: string
  snow: string
  stars: boolean
}

export const KYOTO_LIGHTS: Record<Hour, Light> = {
  night: {
    sky: ['#04050e', '#080a1c', '#0e122a', '#161a38', '#201f44', '#2a2650', '#3a2c58'],
    ridge: ['#141a34', '#0e1328', '#0a0e1e'],
    peak: '#5a6084',
    roof: '#080a14',
    roofLit: '#1a1e30',
    wall: '#141420',
    wood: '#0c0608',
    pagoda: '#2a1014',
    blossom: ['#3a2440', '#5a3a5e', '#7e5478'],
    bamboo: ['#0a1612', '#10221a'],
    lit: 1,
    lantern: 1,
    cloud: '#141830',
    mist: '#262a48',
    snow: '#7a80a4',
    stars: true,
  },
  dusk: {
    sky: ['#140c30', '#2a1446', '#4e1c56', '#82285a', '#b84456', '#e2704c', '#f8a858'],
    ridge: ['#4a2650', '#33193e', '#22102c'],
    peak: '#e8a0a0',
    roof: '#140a18',
    roofLit: '#3a1e2e',
    wall: '#3a2230',
    wood: '#14080a',
    pagoda: '#7a2a24',
    blossom: ['#8a4a6a', '#c87a98', '#f2acc0'],
    bamboo: ['#1a2a22', '#26402e'],
    lit: 0.65,
    lantern: 0.9,
    cloud: '#5a2a4a',
    mist: '#8a5a70',
    snow: '#e0c0cc',
    stars: false,
  },
  dawn: {
    sky: ['#141c40', '#26305c', '#464a78', '#76638a', '#ae7c90', '#da9c94', '#f6c4a4'],
    ridge: ['#4c4c74', '#38395e', '#282a4a'],
    peak: '#f0d0d0',
    roof: '#1a1a2a',
    roofLit: '#3a3a52',
    wall: '#4a4058',
    wood: '#1a0e10',
    pagoda: '#8a3a2e',
    blossom: ['#a8708a', '#d8a0b6', '#f8cada'],
    bamboo: ['#22382e', '#30503c'],
    lit: 0.35,
    lantern: 0.4,
    cloud: '#6a5a7a',
    mist: '#b8a8bc',
    snow: '#e8e8f4',
    stars: false,
  },
  day: {
    sky: ['#4a7cb0', '#5a8cbc', '#6e9cc6', '#84aed0', '#9cbed8', '#b4cede', '#cadade'],
    ridge: ['#6e8aa0', '#52708a', '#3e5a70'],
    peak: '#f4f6fa',
    roof: '#2a3040',
    roofLit: '#4a5468',
    wall: '#8a7e74',
    wood: '#2a1612',
    pagoda: '#b8402a',
    blossom: ['#c87a94', '#eeaac2', '#fcd8e6'],
    bamboo: ['#3a5e3c', '#527a4c'],
    lit: 0.05,
    lantern: 0,
    cloud: '#e8eef4',
    mist: '#d4dee4',
    snow: '#f8f8fc',
    stars: false,
  },
}

function houses(r: () => number, w: number, minW: number, maxW: number, minH: number, maxH: number): House[] {
  const out: House[] = []
  let x = -Math.floor(r() * maxW)
  while (x < w) {
    const hw = minW + Math.floor(r() * (maxW - minW))
    const windows: House['windows'] = []
    for (let wx = 2; wx < hw - 2; wx += 3) if (r() < 0.5) windows.push({ x: wx, k: r() })
    out.push({ x, w: hw, h: minH + Math.floor(r() * (maxH - minH + 1)) + (r() < 0.15 ? 3 : 0), windows, dy: Math.floor(r() * 5) - 2 })
    x += hw - 1 - Math.floor(r() * 3) + (r() < 0.2 ? 3 : 0)
  }
  return out
}

export function makeKyoto(w: number, h: number, seed = 81): Kyoto {
  const r = rng(seed)
  const horizon = Math.round(h * 0.6)
  const floor = Math.round(h * 0.9)
  const noise = valueNoise(seed)
  // Far snowy peaks, then two wooded ridges coming down to the town.
  const ridges = [
    { base: 0.3, amp: 0.18, scale: 70 },
    { base: 0.42, amp: 0.12, scale: 50 },
    { base: 0.5, amp: 0.07, scale: 35 },
  ].map((rd, k) => Array.from({ length: w }, (_, x) => Math.round(h * rd.base - h * rd.amp * (fbm(noise, x / rd.scale + k * 13, k * 5, 4) - 0.3))))
  // Six terraces from the foot of the ridge down to the near street, houses growing as they near.
  const terraces = [-22, -12, -2, 10, 23, 37].map((dy, i) => {
    const k = 0.6 + i * 0.13
    return { base: horizon + dy, houses: houses(r, w, Math.round(7 * k), Math.round(14 * k), Math.round(5 * k), Math.round(8 * k)) }
  })
  // Pagodas: a great one rising from the town on the right, smaller ones on the slopes.
  const pagodas = [
    { k: 0.8, w: 22, floors: 5, base: horizon + 10 },
    { k: 0.15, w: 16, floors: 5, base: horizon - 2 },
    { k: 0.3, w: 7, floors: 3, base: ridges[1][Math.round(w * 0.3)] + 4 },
    { k: 0.93, w: 7, floors: 3, base: ridges[1][Math.round(w * 0.93)] + 4 },
    { k: 0.68, w: 6, floors: 3, base: ridges[2][Math.round(w * 0.68)] + 4 },
  ].map((p) => ({ x: Math.round(w * p.k), base: p.base, w: p.w, floors: p.floors }))
  const hx = Math.round(w * 0.88)
  const hall = { x: hx, base: horizon - 14, w: 30 }
  // The gates climb the second ridge on the left, a red stitch every few pixels.
  const gates: Kyoto['gates'] = []
  // The gates climb from the town up the second ridge, toward the shrine on the left.
  for (let i = 0; i < 16; i++) {
    const x = Math.round(w * 0.22 + i * 5)
    const y = Math.round(horizon - 22 - i * ((horizon - 22 - (ridges[1][Math.min(w - 1, x)] + 6)) / 16) * 1)
    gates.push({ x, y })
  }
  const grove = { x0: Math.round(w * 0.02), x1: Math.round(w * 0.12) }
  const blossoms = Array.from({ length: Math.round(w / 7) }, () => {
    const y = Math.round(horizon - 24 + r() * (floor - horizon + 14))
    return { x: Math.floor(r() * w), y, r: Math.max(2, Math.round(2 + ((y - horizon + 24) / (floor - horizon + 24)) * 5 + r() * 2)) }
  })
  blossoms.sort((a, b) => a.y - b.y)
  const streetLights = Array.from({ length: Math.round(w / 4) }, () => ({ x: Math.floor(r() * w), y: Math.round(horizon - 20 + r() * (floor - horizon + 8)), seed: r() }))
  const skyLanterns = Array.from({ length: 40 }, () => ({ x: r(), speed: 0.5 + r(), seed: r(), far: r() < 0.6 }))
  return { w, h, horizon, floor, ridges, terraces, pagodas, hall, gates, grove, blossoms, streetLights, skyLanterns }
}

/** A tiled roof seen from far: a dark line with eaves lifting at both ends and a light ridge. */
function roof(g: CanvasRenderingContext2D, x: number, y: number, w: number, L: Light, snow: boolean) {
  for (let dx = 0; dx < w; dx++) {
    const u = Math.abs(dx - (w - 1) / 2) / ((w - 1) / 2 || 1)
    const lift = u > 0.8 ? 1 : 0
    g.fillStyle = L.roof
    g.fillRect(x + dx, y - lift, 1, 2)
    g.fillStyle = snow ? L.snow : L.roofLit
    g.fillRect(x + dx, y - lift - 1, 1, 1)
  }
}

function row(g: CanvasRenderingContext2D, list: House[], base: number, L: Light, snow: boolean) {
  for (const hs of list) {
    const b = base + hs.dy
    // From up here a house is mostly its roof: a hipped roof narrowing upward over a short wall.
    const wall = Math.max(1, Math.round(hs.h * 0.3))
    const roofH = hs.h - wall
    g.fillStyle = L.wall
    g.fillRect(hs.x + 1, b - wall, hs.w - 2, wall)
    g.fillStyle = L.wood
    g.fillRect(hs.x + 1, b - 1, hs.w - 2, 1)
    for (let y = 0; y < roofH; y++) {
      const inset = Math.round(((roofH - 1 - y) / Math.max(1, roofH - 1)) * Math.min(hs.w / 3, roofH))
      const top = y === 0
      g.fillStyle = top ? (snow ? L.snow : L.roofLit) : L.roof
      g.fillRect(hs.x + inset, b - wall - roofH + y, hs.w - inset * 2, 1)
      // Light on the sunny slope of the roof.
      if (!top && !snow) {
        g.fillStyle = L.roofLit
        g.fillRect(hs.x + inset, b - wall - roofH + y, 1, 1)
      }
    }
    g.fillStyle = L.roof
    g.fillRect(hs.x - 1, b - wall - 1, 1, 1)
    g.fillRect(hs.x + hs.w, b - wall - 1, 1, 1)
  }
}

function pagoda(g: CanvasRenderingContext2D, p: Kyoto['pagodas'][number], L: Light, snow: boolean) {
  const storey = Math.max(3, Math.round(p.w * 0.42))
  for (let f = 0; f < p.floors; f++) {
    const y = p.base - f * storey
    const bw = Math.max(4, Math.round(p.w * (1 - f * 0.1)))
    // The storey's body in vermilion, then its roof flaring out with lifted corners.
    g.fillStyle = L.pagoda
    g.fillRect(p.x - Math.floor(bw * 0.28), y - storey + 2, Math.max(1, Math.round(bw * 0.56)), storey - 1)
    g.fillStyle = L.roof
    g.fillRect(p.x - Math.floor(bw / 2), y - storey + 1, bw, 2)
    g.fillRect(p.x - Math.floor(bw / 2) - 1, y - storey, 2, 1)
    g.fillRect(p.x + Math.ceil(bw / 2) - 1, y - storey, 2, 1)
    g.fillStyle = snow ? L.snow : L.roofLit
    g.fillRect(p.x - Math.floor(bw / 2) + 1, y - storey + 1, bw - 2, 1)
  }
  g.fillStyle = '#c8a040'
  const top = p.base - p.floors * storey
  g.fillRect(p.x, top - 5, 1, 6)
  for (let i = 0; i < 3; i++) g.fillRect(p.x - 1, top - 4 + i * 2, 3, 1)
}

/** Everything that stands still at an hour and weather. */
function drawStill(g: CanvasRenderingContext2D, k: Kyoto, L: Light, sky: Sky) {
  const snow = sky === 'Snow'
  const img = g.createImageData(k.w, k.horizon + 10)
  ditherBands(img, sky === 'Clear' ? L.sky : L.sky.slice(1, 6), 1.4)
  g.putImageData(img, 0, 0)
  if (L.stars && (sky === 'Clear' || sky === 'Cloudy'))
    for (let i = 0; i < k.w * 0.15; i++) {
      g.fillStyle = hash1(i + 3) > 0.85 ? '#d8dcff' : '#5a608a'
      g.fillRect(Math.floor(hash1(i) * k.w), Math.floor(hash1(i + 40) * k.h * 0.4), 1, 1)
    }
  if ((sky === 'Clear' || sky === 'Cloudy') && L.lantern > 0.5) {
    g.fillStyle = '#2a2a48'
    disc(g, Math.round(k.w * 0.12), Math.round(k.h * 0.14), 7, 6)
    g.fillStyle = '#e8e4d0'
    disc(g, Math.round(k.w * 0.12), Math.round(k.h * 0.14), 5)
    g.fillStyle = '#c8c0b0'
    g.fillRect(Math.round(k.w * 0.12) - 2, Math.round(k.h * 0.14) - 1, 2, 2)
  }
  k.ridges.forEach((ridge, i) => {
    for (let x = 0; x < k.w; x++) {
      g.fillStyle = L.ridge[i]
      g.fillRect(x, ridge[x], 1, k.floor - ridge[x])
      // The far peaks keep snow on their tops; texture of trees on the ridges below.
      if (i === 0 && (snow || ridge[x] < k.h * 0.22)) {
        g.fillStyle = snow ? L.snow : L.peak
        const depth = snow ? 3 : Math.min(4, Math.max(0, Math.round((k.h * 0.22 - ridge[x]) / 3)))
        for (let y = 0; y < depth; y++) if (bayerAt(x, ridge[x] + y) < 1 - y / (depth + 1)) g.fillRect(x, ridge[x] + y, 1, 1)
      }
      if (i > 0 && (x * 7 + i * 3) % 5 === 0) {
        g.fillStyle = L.ridge[Math.min(2, i + 1)]
        g.fillRect(x, ridge[x] + 2 + ((x * 13) % 5), 1, 1)
        if (snow) {
          g.fillStyle = L.snow
          g.fillRect(x, ridge[x], 1, 1)
        }
      }
    }
  })
  // The bamboo grove on the near ridge: a soft green mass, lighter at its crown, streaked by stalks.
  for (let x = k.grove.x0; x < k.grove.x1; x++) {
    const u = (x - k.grove.x0) / (k.grove.x1 - k.grove.x0)
    const top = k.ridges[2][x] - 8 - Math.round(7 * Math.sin(u * Math.PI)) + Math.round(Math.sin(x * 1.7) * 1.5)
    const bottom = k.horizon - 22
    for (let y = top; y < bottom; y++) {
      const v = (y - top) / Math.max(1, bottom - top)
      const stalk = (x * 5) % 7 === 0
      g.fillStyle = stalk ? L.bamboo[0] : v < 0.25 && bayerAt(x, y) < 0.6 - v ? L.bamboo[1] : L.bamboo[0]
      if (!stalk && bayerAt(x + 1, y) > 0.92 - v * 0.2) continue
      g.fillRect(x, y, 1, 1)
    }
  }
  for (const gt of k.gates) {
    g.fillStyle = L.lantern > 0.8 ? '#7a2018' : '#c8401e'
    g.fillRect(gt.x - 2, gt.y - 4, 5, 1)
    g.fillRect(gt.x - 1, gt.y - 3, 1, 4)
    g.fillRect(gt.x + 1, gt.y - 3, 1, 4)
  }
  g.fillStyle = L.ridge[2]
  g.fillRect(0, k.horizon - 24, k.w, k.floor - k.horizon + 24)
  const hallAt = (): void => {
  // The temple hall: a long roof on a stone base.
    const hl = k.hall
    g.fillStyle = L.wall
    g.fillRect(hl.x - 10, hl.base - 5, 20, 5)
    g.fillStyle = L.wood
    for (let x = hl.x - 10; x < hl.x + 10; x += 3) g.fillRect(x, hl.base - 5, 1, 5)
    roof(g, hl.x - hl.w / 2, hl.base - 5, hl.w, L, snow)
    roof(g, hl.x - 8, hl.base - 9, 16, L, snow)
  }
  // The town: terraces of houses down the hill, cherry trees among them.
  // Landmarks stand in the row they belong to: whatever is nearer is drawn over them.
  const landmarks = (from: number, to: number) => {
    for (const p of k.pagodas) if (p.base > from && p.base <= to) pagoda(g, p, L, snow)
    if (k.hall.base > from && k.hall.base <= to) hallAt()
  }
  landmarks(-1, k.terraces[0].base - 1)
  k.terraces.forEach((tr, i) => {
    const prev = k.terraces[i - 1]?.base ?? -1
    g.fillStyle = L.ridge[2]
    g.fillRect(0, tr.base, k.w, 4)
    for (const b of k.blossoms) if (b.y <= tr.base && b.y > prev) blossom(g, b, L, snow)
    row(g, tr.houses, tr.base, L, snow)
    landmarks(tr.base - 1, (k.terraces[i + 1]?.base ?? k.floor) - 1)
  })
  for (const b of k.blossoms) if (b.y > k.terraces[k.terraces.length - 1].base) blossom(g, b, L, snow)
  // The veranda we look from: a dark rail and its posts.
  g.fillStyle = '#050304'
  g.fillRect(0, k.floor, k.w, k.h - k.floor)
  g.fillRect(0, k.floor - 9, k.w, 2)
  for (let x = 3; x < k.w; x += 11) g.fillRect(x, k.floor - 9, 2, 9)
  g.fillStyle = L.lantern > 0.5 ? '#2a1410' : '#3a2018'
  g.fillRect(0, k.floor - 10, k.w, 1)
  g.fillRect(0, k.floor, k.w, 1)
  if (snow) {
    g.fillStyle = L.snow
    g.fillRect(0, k.floor - 11, k.w, 1)
  }
}

function blossom(g: CanvasRenderingContext2D, b: Kyoto['blossoms'][number], L: Light, snow: boolean) {
  for (let dy = -b.r; dy <= Math.ceil(b.r * 0.4); dy++)
    for (let dx = -b.r - 1; dx <= b.r + 1; dx++) {
      const d = (dx / (b.r + 1)) ** 2 + (dy / b.r) ** 2
      if (d > 1 || bayerAt(b.x + dx, b.y + dy) > 1 - d * 0.5) continue
      const lit = -dx - dy
      g.fillStyle = snow && dy < 0 ? L.snow : L.blossom[lit > 1 ? 2 : lit > -2 ? 1 : 0]
      g.fillRect(b.x + dx, b.y + dy, 1, 1)
    }
}

/** Where a sky lantern is: it rises from the town, sways, and starts again. */
export function skyLanternAt(l: Kyoto['skyLanterns'][number], k: Kyoto, t: number): { x: number; y: number } {
  const span = k.horizon + 40
  const p = ((t * l.speed * (l.far ? 1.5 : 3) + l.seed * span) % span) / span
  return { x: Math.round(l.x * k.w + Math.sin(t * 0.3 + l.seed * 9) * 4 + p * 10), y: Math.round(k.horizon + 30 - p * span) }
}

const still = new LayerCache()
const fogs = new LayerCache()

export function drawKyoto(g: CanvasRenderingContext2D, k: Kyoto, time: number, calm: boolean, weather: Weather) {
  const t = calm ? 0 : time
  const L = KYOTO_LIGHTS[weather.hour]
  const sky = weather.sky
  g.drawImage(still.get('kyoto', `${weather.hour}:${sky}`, k.w, k.h, (c) => drawStill(c, k, L, sky)), 0, 0)

  // Clouds drifting across, more in bad weather: a few lumps each, dithered at the edges.
  const clouds = sky === 'Clear' ? 2 : sky === 'Cloudy' || sky === 'Storm' ? 6 : 4
  g.fillStyle = sky === 'Storm' ? '#0a0a16' : L.cloud
  for (let i = 0; i < clouds; i++) {
    const cw = k.w * (0.06 + hash1(i) * 0.08)
    const cy = Math.round(k.h * (0.05 + hash1(i + 9) * 0.2))
    const x0 = ((hash1(i + 3) * k.w + t * (1 + hash1(i) * 2)) % (k.w + cw * 2)) - cw
    for (let j = 0; j < 4; j++) {
      const cx = x0 + (j / 3) * cw
      const rr = (cw / 5) * (j === 1 || j === 2 ? 1.2 : 0.8)
      for (let y = -Math.ceil(rr); y <= rr * 0.5; y++)
        for (let x = -Math.ceil(rr * 1.4); x <= rr * 1.4; x++) {
          const d = (x / (rr * 1.4)) ** 2 + (y / rr) ** 2
          const px = Math.round(cx + x)
          const py = cy + y
          if (d < 1 && bayerAt(px, py) < (1 - d) * 1.1) g.fillRect(px, py, 1, 1)
        }
    }
  }

  // Lit windows and street lanterns in the town.
  if (L.lit > 0.1) {
    for (const { houses: list, base } of k.terraces)
      for (const hs of list)
        for (const win of hs.windows) {
          if (win.k > L.lit) continue
          g.fillStyle = win.k < 0.2 && Math.sin(t * 0.8 + hs.x) > 0.8 ? '#a06030' : '#ffc070'
          g.fillRect(hs.x + win.x, base + hs.dy - Math.max(1, Math.round(hs.h * 0.3)), 1, 1)
        }
    for (const p of k.pagodas) {
      g.fillStyle = '#ffb060'
      const storey = Math.max(3, Math.round(p.w * 0.42))
      for (let f = 0; f < p.floors; f++) if (Math.sin(t * 0.5 + f + p.x) > -0.5) g.fillRect(p.x, p.base - f * storey - 2, 1, 1)
    }
  }
  if (L.lantern > 0.3)
    for (const s of k.streetLights) {
      const on = calm || Math.sin(t * (2 + s.seed * 3) + s.seed * 40) > -0.7
      g.fillStyle = on ? '#ff9a40' : '#a05020'
      g.fillRect(s.x, s.y, 1, 1)
    }

  // Incense smoke from the temple hall, thin and slow.
  g.fillStyle = L.mist
  for (let i = 0; i < 18; i++) {
    const age = ((t * 0.08 + i / 18) % 1)
    const x = Math.round(k.hall.x + Math.sin(age * 6 + i) * 2 + age * 6)
    const y = Math.round(k.hall.base - 10 - age * 30)
    if (bayerAt(x, y) < 0.7 * (1 - age)) g.fillRect(x, y, 1, 1)
  }

  // Birds crossing at dawn and by day: small Vs that flap.
  if ((weather.hour === 'dawn' || weather.hour === 'day') && sky !== 'Storm' && sky !== 'Rain')
    for (let i = 0; i < 6; i++) {
      const x = Math.round(((t * 9 + i * 7 + hash1(i) * 40) % (k.w + 40)) - 20)
      const y = Math.round(k.h * 0.22 + i * 3 + Math.sin(t + i) * 2)
      const up = Math.sin(t * 8 + i) > 0
      g.fillStyle = '#1a1a2a'
      g.fillRect(x, y, 1, 1)
      g.fillRect(x - 1, y - (up ? 1 : 0), 1, 1)
      g.fillRect(x + 1, y - (up ? 1 : 0), 1, 1)
    }

  // Sky lanterns rising from the town after dusk, in calm weather.
  if (L.lantern > 0.3 && sky !== 'Rain' && sky !== 'Storm' && sky !== 'Drizzle')
    for (const l of k.skyLanterns) {
      const { x, y } = skyLanternAt(l, k, t)
      if (Math.abs(x - k.w / 2) < k.w * 0.13 && y < k.floor * 0.7 && y > k.h * 0.08) continue
      const flick = calm ? 1 : 0.85 + 0.15 * Math.sin(t * 6 + l.seed * 30)
      g.fillStyle = l.far ? `rgba(255, 170, 90, ${0.75 * flick})` : '#ffb860'
      g.fillRect(x, y, 1, l.far ? 1 : 2)
      if (!l.far) {
        g.fillStyle = `rgba(255, 140, 60, ${0.3 * flick})`
        g.fillRect(x - 1, y - 1, 3, 4)
      }
    }

  if (sky === 'Fog') {
    const f1 = wavesAcross(k.w, 0.04)
    const band = fogs.get('fog', weather.hour, k.w, 30, (c) => {
      c.fillStyle = L.mist
      for (let y = 0; y < 30; y++)
        for (let x = 0; x < k.w; x++) if (bayerAt(x, y) < (1 - Math.abs(y - 15) / 15) * (0.5 + 0.25 * Math.sin(x * f1 + y * 0.2))) c.fillRect(x, y, 1, 1)
    })
    const dx = Math.round(t * 2) % k.w
    for (const top of [k.horizon - 40, k.horizon - 8]) {
      g.drawImage(band, dx, top)
      g.drawImage(band, dx - k.w, top)
    }
  }

  // What falls: petals in calm weather, snow, rain.
  if ((sky === 'Clear' || sky === 'Cloudy') && weather.hour !== 'night')
    for (let i = 0; i < 18; i++) {
      const s = hash1(i)
      g.fillStyle = L.blossom[1 + (i % 2)]
      g.fillRect(Math.round((s * k.w + t * (6 + s * 5) + Math.sin(t * 1.3 + i) * 5) % k.w), Math.round((hash1(i + 50) * k.h + t * (5 + s * 4)) % k.h), 1, 1)
    }
  if (sky === 'Snow')
    for (let i = 0; i < Math.round(k.w * 0.3); i++) {
      const s = hash1(i)
      g.fillStyle = s > 0.7 ? '#ffffff' : '#c8c8dc'
      g.fillRect(Math.round((s * k.w + Math.sin(t * 0.7 + i) * 3) % k.w), Math.round((hash1(i + 70) * k.h + t * (7 + s * 8)) % k.h), 1, 1)
    }
  const rain = sky === 'Storm' ? 60 : sky === 'Rain' ? 32 : sky === 'Drizzle' ? 12 : 0
  if (rain) {
    g.fillStyle = weather.hour === 'day' ? '#8aa0b8' : '#3e4470'
    for (let i = 0; i < Math.round((rain * k.w) / 100); i++) g.fillRect(Math.round((hash1(i) * k.w + t * 20) % k.w), Math.round((hash1(i + 30) * k.h + t * 90) % k.h), 1, sky === 'Drizzle' ? 1 : 2)
  }
  if (sky === 'Storm' && !calm && t % 7 < 0.08) {
    g.fillStyle = 'rgba(220, 220, 255, 0.3)'
    g.fillRect(0, 0, k.w, k.horizon)
  }
}

export function kyotoScene(w: number, h: number): Scene {
  const k = makeKyoto(w, h)
  return { w, h, draw: (g, time, calm, weather) => drawKyoto(g, k, time, calm, weather) }
}
