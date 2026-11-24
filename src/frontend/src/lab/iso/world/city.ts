import { box, iso, onLeftFace, onRightFace, shades, SQUASH, YF } from '../iso'
import { dot, mirrored, type Prop } from '../props'
import { mix, noise, Painter, rand } from '../px'
import { scientistFrames, type Facing, type Frames, type Look } from '../scientist'
import { hashOf, type Plan } from './layout'
import { streetLamp, tree } from './outdoor'

/**
 * The city round the lab, a bright and green one: a grid of streets with two lanes each way,
 * pavements, zebra crossings and traffic lights at every crossing; blocks of white and glass
 * buildings with gardens on their roofs, parks, squares. Buildings stay low near the lab and in
 * front of it, taller behind: the lab is what you look at. Cars drive in all four directions and
 * stop at red lights; people walk the pavements and cross on green.
 */

const T = 8
const ROAD = 4
const BLOCK = 16
const PERIOD = BLOCK + ROAD
/** Traffic lights: the a streets have green, then amber, then the b streets do. */
const CYCLE = 15

export type Axis = 'a' | 'b'

export function lightFor(axis: Axis, t: number): 'green' | 'amber' | 'red' {
  const k = ((t % CYCLE) + CYCLE) % CYCLE
  if (axis === 'a') return k < 6 ? 'green' : k < 7.5 ? 'amber' : 'red'
  return k >= 7.5 && k < 13.5 ? 'green' : k >= 13.5 ? 'amber' : 'red'
}

export interface CityThing {
  prop: Prop
  a: number
  b: number
  flip: number
}

export interface City {
  /** Colour of the ground at a point outside the building, in floor units. */
  ground(a: number, b: number): string
  things: CityThing[]
  /** Moves the traffic on; returns what moves, to be drawn this frame. */
  move(t: number, dt: number): CityThing[]
}

/** Street bands (start tile) along one axis: one hugging the lab's square on each side, then every PERIOD. */
function bands(lo: number, hi: number, size: number): number[] {
  const out: number[] = []
  for (let s = lo - ROAD; s > -PERIOD; s -= PERIOD) out.push(s)
  for (let s = hi; s < size + PERIOD; s += PERIOD) out.push(s)
  return out.sort((x, y) => x - y)
}

const inBand = (list: number[], t: number) => list.find((s) => t >= s && t < s + ROAD)

/** A building of the bright city: a glass podium, a white tower banded with windows, a garden or solar roof. */
function building(da: number, db: number, height: number, seed: number): Prop {
  const r = rand(seed)
  const extra = 8
  const w = 2 * (da + db) + 2 * extra
  const top = height + 16
  const h = Math.ceil((da + db) * YF) + top + 2 * extra
  const ax = 2 * db + extra
  const ay = top + extra
  const p = new Painter(w, h)
  const at = (a: number, b: number, z = 0): [number, number] => {
    const [x, y] = iso(a, b, z)
    return [ax + x, ay + y]
  }
  const facade = ['#eef2f5', '#e4ecef', '#f2efe8', '#e8eef4'][Math.floor(r() * 4)]
  const glass = ['#4b6a80', '#3f6a6e', '#556079'][Math.floor(r() * 3)]
  const accent = ['#5fd7e6', '#7be3a8', '#ffd27a', '#9ab8ff'][Math.floor(r() * 4)]
  p.poly([at(0, 0), at(da + 3, 0), at(da + 3, db + 3), at(0, db + 3)], '#0b1220', 0.2)
  // The podium: a shop front of glass, lit at night.
  // The street floor is as tall as a person and a half: shops with doors you could walk through.
  const pod = Math.min(height, 40)
  box(p, at, 0, 0, 0, da, db, pod, { top: '#cfd8df', left: '#2c3b4a', right: '#243241' })
  p.emitting = true
  for (let u = 2; u < 2 * da - 2; u++) for (let v = 4; v < pod - 6; v++) if (u % 18 > 1) onLeftFace(p, at, 0, db, 0, u, v, v === pod - 7 ? accent : v > pod - 12 ? '#ffe9c0' : '#ffe2a8', 0.75)
  for (let u = 2; u < 2 * db - 2; u++) for (let v = 4; v < pod - 6; v++) if (u % 18 > 1) onRightFace(p, at, da, 0, 0, u, v, '#ffe2a8', 0.75)
  p.emitting = false
  // The tower, a little set back, banded with windows; some lit, some dark, by the seed.
  const inset = 2
  const tda = da - 2 * inset
  const tdb = db - 2 * inset
  const tower = height - pod
  box(p, at, inset, inset, pod, tda, tdb, tower, shades(facade))
  const lit = (u: number, v: number, face: number) => noise(u / 6 + face * 50, Math.floor(v / 22), seed) > 0.55
  // Floors of 22 pixels: a band of glass, a strip of facade.
  for (let v = 5; v < tower - 3; v++) {
    const band = v % 22 > 5 && v % 22 < 19
    if (!band) continue
    for (let u = 1; u < 2 * tda - 1; u++) {
      if (u % 6 === 0) continue
      onLeftFace(p, at, inset, inset + tdb, pod, u, v, mix(glass, '#ffffff', 0.08))
      if (lit(u, v, 0)) {
        p.emitting = true
        onLeftFace(p, at, inset, inset + tdb, pod, u, v, r() > 0.3 ? '#ffe0a0' : '#bfe8ff', 0.85)
        p.emitting = false
        onLeftFace(p, at, inset, inset + tdb, pod, u, v, mix(glass, '#ffffff', 0.08))
      }
    }
    for (let u = 1; u < 2 * tdb - 1; u++) {
      if (u % 6 === 0) continue
      onRightFace(p, at, inset + tda, inset, pod, u, v, mix(glass, '#000000', 0.15))
      if (lit(u, v, 1)) {
        p.emitting = true
        onRightFace(p, at, inset + tda, inset, pod, u, v, '#ffe0a0', 0.8)
        p.emitting = false
        onRightFace(p, at, inset + tda, inset, pod, u, v, mix(glass, '#000000', 0.15))
      }
    }
  }
  // A vertical garden down one corner, now and then.
  if (r() > 0.5) for (let v = 2; v < tower - 2; v++) for (let u = 1; u < 5; u++) onLeftFace(p, at, inset, inset + tdb, pod, u, v, ['#3d7346', '#4f8c50', '#6aa65e'][(u + v * 3) % 3])
  // The roof: a garden with small trees, or solar panels; a strip of light round its edge.
  const roofZ = pod + tower
  if (r() > 0.45) {
    p.poly([at(inset + 1, inset + 1, roofZ), at(inset + tda - 1, inset + 1, roofZ), at(inset + tda - 1, inset + tdb - 1, roofZ), at(inset + 1, inset + tdb - 1, roofZ)], (x, y) =>
      noise(x / 2, y / 2, seed) > 0.6 ? '#6aa65e' : '#4f8c50',
    )
    for (let k = 0; k < Math.max(1, Math.floor((tda * tdb) / 60)); k++) {
      const [x, y] = at(inset + 2 + r() * (tda - 4), inset + 2 + r() * (tdb - 4), roofZ)
      p.ellipse(x, y - 3, 3, 3, (px, py) => (py < y - 4 && px < x ? '#8cc070' : '#3d7346'))
    }
  } else {
    for (let a = inset + 2; a < inset + tda - 3; a += 5)
      for (let b = inset + 2; b < inset + tdb - 3; b += 4)
        p.poly([at(a, b, roofZ + 1), at(a + 4, b, roofZ + 1), at(a + 4, b + 3, roofZ + 2), at(a, b + 3, roofZ + 2)], (x, y) => ((x + y) % 3 === 0 ? '#5a7fb8' : '#2c4470'))
  }
  p.emitting = true
  for (let u = 0; u < 2 * tda; u++) onLeftFace(p, at, inset, inset + tdb, roofZ - 1, u, 0, accent, 0.9)
  for (let u = 0; u < 2 * tdb; u++) onRightFace(p, at, inset + tda, inset, roofZ - 1, u, 0, accent, 0.9)
  p.emitting = false
  p.outline('#18202b')
  return { da, db, sprite: { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined } }
}

/** A traffic light on a corner: two heads, one for each street; their lamps are live. */
function trafficLight(): Prop {
  const w = 22
  const h = 50
  const ax = 11
  const ay = 46
  const p = new Painter(w, h)
  const at = (a: number, b: number, z = 0): [number, number] => {
    const [x, y] = iso(a, b, z)
    return [ax + x, ay + y]
  }
  p.ellipse(ax + 1, ay, 3, 3 * SQUASH, '#0b1220', 0.25)
  box(p, at, -0.5, -0.5, 0, 1, 1, 30, shades('#3a4352'))
  box(p, at, -1, -1, 30, 2, 3, 9, shades('#22282f'))
  box(p, at, -1, -1, 30, 3, 2, 9, shades('#22282f'))
  p.outline('#18202b')
  const heads: Record<Axis, [number, number][]> = { a: [], b: [] }
  for (let k = 0; k < 3; k++) {
    heads.b.push(at(-1, 2, 37 - k * 3).map((v, i) => v - (i === 0 ? ax : ay)) as [number, number])
    heads.a.push(at(2, -1, 37 - k * 3).map((v, i) => v - (i === 0 ? ax : ay)) as [number, number])
  }
  return {
    da: 2,
    db: 2,
    sprite: { canvas: p.canvas(), ax, ay },
    anim(ctx, x, y, t) {
      for (const axis of ['a', 'b'] as Axis[]) {
        const state = lightFor(axis, t)
        const colours = ['#ff4a4a', '#ffbf3a', '#4dff8a']
        const on = state === 'red' ? 0 : state === 'amber' ? 1 : 2
        heads[axis].forEach(([dx, dy], k) => dot(ctx, x + dx, y + dy, k === on ? colours[k] : '#2a3038', 1, 2))
      }
    },
  }
}

/** A car seen from the front (driving towards you along +a) or the back (driving away along -a). */
export function carOf(colour: string, view: 'front' | 'back' | 'parked'): Prop {
  const da = 30
  const db = 13
  const extra = 6
  const top = 22
  const w = 2 * (da + db) + 2 * extra
  const h = Math.ceil((da + db) * YF) + top + 2 * extra
  const ax = 2 * db + extra
  const ay = top + extra
  const p = new Painter(w, h)
  const at = (a: number, b: number, z = 0): [number, number] => {
    const [x, y] = iso(a, b, z)
    return [ax + x, ay + y]
  }
  p.poly([at(-1, -1), at(da + 2, -1), at(da + 2, db + 2), at(-1, db + 2)], '#0b1220', 0.28)
  // Wheels, then a low rounded body, a long glass canopy, a light bar front and back.
  for (const [a, b] of [
    [6, db],
    [24, db],
    [24, 0],
  ]) {
    const [x, y] = at(a, b, 3)
    p.ellipse(x, y, 3.5, 3.2, '#15181e')
    p.set(x, y, '#5d6878')
  }
  box(p, at, 0, 0, 2, da, db, 7, shades(colour))
  box(p, at, 1, 1, 9, da - 2, db - 2, 1, shades(mix(colour, '#ffffff', 0.1)), false)
  box(p, at, 7, 2, 10, 15, db - 4, 6, { top: mix(colour, '#ffffff', 0.2), left: '#2b3a4c', right: '#22303f' })
  // A stripe along the side.
  for (let u = 2; u < 2 * da - 2; u++) onLeftFace(p, at, 0, db, 2, u, 3, mix(colour, '#ffffff', 0.35))
  p.emitting = view !== 'parked'
  for (let u = 2; u < 2 * db - 2; u++) onRightFace(p, at, da, 0, 2, u, 5, view === 'back' ? '#ff4a5a' : view === 'front' ? '#fff4c8' : '#c9d1da')
  p.emitting = false
  p.outline('#18202b')
  const [lx, ly] = iso(da + 14, db / 2)
  return { da, db, sprite: { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined }, lights: view === 'front' ? [{ dx: lx, dy: ly, r: 24, color: '#fff0c0' }] : undefined }
}

interface Mover {
  axis: Axis
  dir: 1 | -1
  /** Where along its axis, and its fixed place across. */
  at: number
  across: number
  speed: number
  len: number
  prop: Prop
  person?: { frames: Frames; walked: number }
}

const COATS = ['#3a5f9a', '#c2443e', '#e0b14a', '#4c9a7a', '#6c5a8a', '#2b2f36', '#d8d0c0', '#e07a5f']
const SKINS = ['#f1c9a5', '#e8b48f', '#8d5a3b', '#f6d7c3', '#c98e66', '#5e3b28']
const HAIRS = ['#1d1a22', '#5a3424', '#141018', '#c9a06a', '#2a2a33', '#8a4a2a']
const CARS = ['#e8ecf0', '#3a5f9a', '#5fd7e6', '#c2443e', '#2b2f36', '#7be3a8', '#e0b14a']

export function makeCity(plan: Plan): City {
  const [ba0, bb0, ba1, bb1] = plan.box
  // The lab's square: three tiles round the building.
  const lot = [ba0 - 3, bb0 - 3, ba1 + 3, bb1 + 3]
  const alongA = bands(lot[1], lot[3], plan.h) // streets running along a, by their b
  const alongB = bands(lot[0], lot[2], plan.w) // streets running along b, by their a
  const seed = Math.floor(hashOf(`${plan.w}x${plan.h}`, 3) * 1e6)
  const NA = plan.w * T
  const NB = plan.h * T

  const blockOf = (ta: number, tb: number) => {
    const ia = alongB.filter((s) => s + ROAD <= ta).length
    const ib = alongA.filter((s) => s + ROAD <= tb).length
    return [ia, ib]
  }
  const labBlock = blockOf((lot[0] + lot[2]) >> 1, (lot[1] + lot[3]) >> 1)
  const kindOf = (ia: number, ib: number) => {
    if (ia === labBlock[0] && ib === labBlock[1]) return 'lab'
    const h = hashOf(`${ia},${ib}`, seed)
    const near = Math.abs(ia - labBlock[0]) + Math.abs(ib - labBlock[1]) === 1
    if (near) return h < 0.5 ? 'park' : 'low'
    return h < 0.22 ? 'park' : h < 0.3 ? 'square' : 'tall'
  }

  const ground = (a: number, b: number): string => {
    const ta = Math.floor(a / T)
    const tb = Math.floor(b / T)
    const sa = inBand(alongB, ta)
    const sb = inBand(alongA, tb)
    if (sa !== undefined || sb !== undefined) {
      // The road: asphalt, lane lines, zebra crossings next to the crossings, stop lines.
      const asphalt = noise(a / 3, b / 3, 5) > 0.72 ? '#454a54' : '#3e434c'
      if (sa !== undefined && sb !== undefined) return asphalt
      if (sb !== undefined) {
        const mid = (sb + ROAD / 2) * T
        const near = alongB.find((s) => ta >= s - 2 && ta < s + ROAD + 2)
        if (near !== undefined) return Math.floor(b / 2.5) % 2 === 0 ? '#e9e6da' : asphalt
        if (Math.abs(b - mid) < 0.6 && Math.floor(a / 10) % 2 === 0) return '#e6dfb8'
        return asphalt
      }
      const mid = (sa! + ROAD / 2) * T
      const near = alongA.find((s) => tb >= s - 2 && tb < s + ROAD + 2)
      if (near !== undefined) return Math.floor(a / 2.5) % 2 === 0 ? '#e9e6da' : asphalt
      if (Math.abs(a - mid) < 0.6 && Math.floor(b / 10) % 2 === 0) return '#e6dfb8'
      return asphalt
    }
    // Pavements along every street: light stone, a kerb.
    const nearA = alongB.some((s) => ta === s - 1 || ta === s + ROAD)
    const nearB = alongA.some((s) => tb === s - 1 || tb === s + ROAD)
    if (nearA || nearB) {
      const kerb = alongB.some((s) => Math.abs(a - s * T) < 0.8 || Math.abs(a - (s + ROAD) * T) < 0.8) || alongA.some((s) => Math.abs(b - s * T) < 0.8 || Math.abs(b - (s + ROAD) * T) < 0.8)
      if (kerb) return '#9a968c'
      return a % 8 < 0.5 || b % 8 < 0.5 ? '#b4afa4' : '#c8c3b8'
    }
    const [ia, ib] = blockOf(ta, tb)
    const kind = kindOf(ia, ib)
    if (kind === 'lab' || kind === 'square') {
      const seam = a % 8 < 0.5 || b % 8 < 0.5
      return seam ? '#b9b3a8' : (ta + tb) % 2 ? '#d6d1c6' : '#d0cbc0'
    }
    if (kind === 'park') {
      const path = Math.abs(((a + b) % 96) - 48) < 4 || Math.abs(((a - b + 960) % 96) - 48) < 4
      if (path) return '#d2c6a8'
      const n = noise(a / 9, b / 9, 13) * 0.9 + 0.55 * noise(a / 2.2, b / 2.2, 29)
      const speck = noise(a * 1.7, b * 1.7, 43)
      if (speck > 0.95) return speck > 0.975 ? '#e8d77a' : '#e3a9c8'
      return n > 1.05 ? '#75a560' : n > 0.8 ? '#659552' : n > 0.55 ? '#578747' : '#4b793e'
    }
    // Round the buildings: a strip of lawn and paving.
    return noise(a / 4, b / 4, 9) > 0.55 ? '#8fb27a' : '#a9c38f'
  }

  // What stands in the blocks.
  const things: CityThing[] = []
  const r = rand(seed)
  const edges = (list: number[], size: number) => {
    const out: [number, number][] = []
    let prev = -PERIOD
    for (const s of [...list, size + PERIOD]) {
      out.push([prev + ROAD + 1, s - 1])
      prev = s
    }
    return out.filter(([x, y]) => y - x >= 4)
  }
  const labSum = (lot[2] + lot[3]) * T
  for (const [x0, x1] of edges(alongB, plan.w)) {
    for (const [y0, y1] of edges(alongA, plan.h)) {
      const [ia, ib] = blockOf(x0, y0)
      const kind = kindOf(ia, ib)
      if (kind === 'lab') continue
      const k = Math.floor(hashOf(`${ia},${ib}`, seed + 1) * 1e6)
      if (kind === 'park' || kind === 'square') {
        const n = kind === 'park' ? 10 + Math.floor(r() * 8) : 4
        for (let i = 0; i < n; i++) {
          const a = (x0 + 1 + r() * (x1 - x0 - 2)) * T
          const b = (y0 + 1 + r() * (y1 - y0 - 2)) * T
          things.push({ prop: tree(k + i * 17), a, b, flip: 1 })
        }
        continue
      }
      // One to four buildings, in the block's own grid.
      const near = kind === 'low'
      const splitA = x1 - x0 > 9 && (near || r() > 0.35)
      const splitB = y1 - y0 > 9 && (near || r() > 0.35)
      const cutsA = splitA ? [x0, Math.floor((x0 + x1) / 2), x1] : [x0, x1]
      const cutsB = splitB ? [y0, Math.floor((y0 + y1) / 2), y1] : [y0, y1]
      for (let i = 0; i < cutsA.length - 1; i++) {
        for (let j = 0; j < cutsB.length - 1; j++) {
          const a0 = cutsA[i] + 1
          const b0 = cutsB[j] + 1
          const da = (cutsA[i + 1] - cutsA[i] - 2) * T
          const db = (cutsB[j + 1] - cutsB[j] - 2) * T
          if (da < 24 || db < 24) continue
          // Low near the lab and in front of it; taller behind and further away.
          const far = Math.hypot(ia - labBlock[0], ib - labBlock[1])
          const front = (a0 + b0) * T > labSum - 8
          // Floors above the street one: none in front of the lab, one or two beside it, more further off.
          const floors = front ? 0 : near ? 1 + Math.floor(r() * 2) : Math.min(6, 1 + Math.round(far * (0.7 + r())))
          const height = 40 + floors * 22
          things.push({ prop: building(da, db, height, k + i * 7 + j * 13), a: a0 * T, b: b0 * T, flip: 1 })
        }
      }
    }
  }
  // The lab's square: trees in planters along its edge, lamps.
  for (let a = lot[0]; a < lot[2]; a += 4)
    for (const b of [lot[1], lot[3] - 1]) {
      const [ba, bb] = [Math.floor(a), b]
      if (ba > plan.box[0] - 2 && ba < plan.box[2] + 1 && bb >= plan.box[1] - 1 && bb <= plan.box[3]) continue
      things.push({ prop: tree(seed + a * 31 + b), a: a * T + 1, b: b * T + 1, flip: 1 })
    }
  for (let b = lot[1] + 4; b < lot[3] - 1; b += 4) for (const a of [lot[0], lot[2] - 1]) things.push({ prop: tree(seed + a * 37 + b), a: a * T + 1, b: b * T + 1, flip: 1 })
  // Street lamps and traffic lights.
  for (const s of alongA)
    for (let a = 2; a < plan.w; a += 7) if (inBand(alongB, a) === undefined && inBand(alongB, a + 1) === undefined) things.push({ prop: streetLamp(), a: a * T + 4, b: (s + ROAD) * T + 3, flip: 1 })
  for (const sa of alongB) {
    for (const sb of alongA) {
      things.push({ prop: trafficLight(), a: (sa - 1) * T + 3, b: (sb - 1) * T + 3, flip: 1 })
      things.push({ prop: trafficLight(), a: (sa + ROAD) * T + 3, b: (sb + ROAD) * T + 3, flip: 1 })
    }
  }

  // Traffic: cars in both lanes of every street, people on both pavements.
  const front = CARS.map((c) => carOf(c, 'front'))
  const back = CARS.map((c) => carOf(c, 'back'))
  const frontB = front.map(mirrored)
  const backB = back.map(mirrored)
  const movers: Mover[] = []
  const lookFor = (): Look => ({
    skin: SKINS[Math.floor(r() * SKINS.length)],
    hair: HAIRS[Math.floor(r() * HAIRS.length)],
    style: (['short', 'long', 'curly', 'bun', 'pony'] as const)[Math.floor(r() * 5)],
    shirt: COATS[Math.floor(r() * COATS.length)],
    coat: COATS[Math.floor(r() * COATS.length)],
    trousers: r() > 0.5 ? '#2e3644' : '#5a5f6a',
  })
  for (const [axis, list, size] of [
    ['a', alongA, NA],
    ['b', alongB, NB],
  ] as [Axis, number[], number][]) {
    for (const s of list) {
      for (const dir of [1, -1] as const) {
        const across = s * T + (dir === 1 ? 18 : 2)
        const n = 1 + Math.floor(r() * 2)
        for (let i = 0; i < n; i++) {
          const c = Math.floor(r() * CARS.length)
          const prop = axis === 'a' ? (dir === 1 ? front[c] : back[c]) : dir === 1 ? frontB[c] : backB[c]
          movers.push({ axis, dir, at: r() * size, across, speed: 30 + r() * 14, len: 30, prop })
        }
        // People on the pavement on this side.
        const walkAcross = dir === 1 ? (s + ROAD) * T + 4 : s * T - 4
        for (let i = 0; i < 2; i++) {
          const look = lookFor()
          movers.push({
            axis,
            dir: r() > 0.5 ? 1 : -1,
            at: r() * size,
            across: walkAcross,
            speed: 8 + r() * 4,
            len: 4,
            prop: { da: 2, db: 2, sprite: { canvas: document.createElement('canvas'), ax: 0, ay: 0 } },
            person: { frames: scientistFrames(look), walked: 0 },
          })
        }
      }
    }
  }
  const crossings = (axis: Axis) => (axis === 'a' ? alongB : alongA).map((s) => [s * T, (s + ROAD) * T] as [number, number])

  return {
    ground,
    things,
    move(t, dt) {
      const out: CityThing[] = []
      for (const m of movers) {
        const size = m.axis === 'a' ? NA : NB
        const green = lightFor(m.axis, t) === 'green'
        // Where it would stop: before the zebra (cars) or at the kerb (people).
        const gap = m.person ? 8 : 18
        let step = m.speed * dt
        if (!green) {
          const nose = m.dir === 1 ? m.at + m.len : m.at
          for (const [c0, c1] of crossings(m.axis)) {
            const line = m.dir === 1 ? c0 - gap : c1 + gap
            const ahead = (line - nose) * m.dir
            if (ahead >= -0.5 && ahead < step + (lightFor(m.axis, t) === 'amber' ? 6 : 14)) step = Math.max(0, Math.min(step, ahead - 0.5))
          }
        }
        // Keep a distance from whoever is ahead in the same lane.
        if (!m.person) {
          for (const o of movers) {
            if (o === m || o.person || o.axis !== m.axis || o.across !== m.across) continue
            const d = (o.at - m.at) * m.dir
            if (d > 0 && d < m.len + 8 + step) step = Math.max(0, Math.min(step, d - m.len - 8))
          }
        }
        m.at += step * m.dir
        if (m.at > size + 40) m.at = -40
        if (m.at < -40) m.at = size + 40
        if (m.person) {
          m.person.walked += step / m.speed
          const facing: Facing = m.axis === 'a' ? (m.dir === 1 ? 'SE' : 'NW') : m.dir === 1 ? 'SW' : 'NE'
          const moving = step > 0.01
          const canvas = m.person.frames.get(facing, moving ? 'walk' : 'stand', Math.floor(m.person.walked * 8))
          const a = m.axis === 'a' ? m.at : m.across
          const b = m.axis === 'a' ? m.across : m.at
          out.push({ prop: { da: 2, db: 2, sprite: { canvas, ax: 16, ay: 55 } }, a: a - 1, b: b - 1, flip: 1 })
          continue
        }
        const a = m.axis === 'a' ? m.at : m.across
        const b = m.axis === 'a' ? m.across : m.at
        out.push({ prop: m.prop, a, b, flip: m.axis === 'b' ? -1 : 1 })
      }
      return out
    },
  }
}
