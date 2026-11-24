import { box, iso, shades, SQUASH } from '../iso'
import { dot, mirrored, type Prop } from '../props'
import { noise, Painter, rand } from '../px'
import { scientistFrames, type Facing, type Frames, type Look } from '../scientist'
import { building, type Kind } from './buildings'
import { hashOf, type Plan } from './layout'
import { streetLamp, tree } from './outdoor'
import { bench, bikeRack, fountain, planter, sandpit, seesaw, slide, swings } from './playground'
import { vehicle, type Make } from './vehicles'

/**
 * The city round the lab, a bright one, and an untidy one, as cities are: streets two lanes each
 * way at uneven distances, so blocks come in all sizes; zebra crossings and traffic lights at
 * every crossing. A block may be a park (a thick wood with paths, a playground, benches), a
 * square, or buildings of several kinds on plots of uneven size, with yards, car parks, gaps.
 * Buildings stay low by the lab and in front of it, taller behind it. Trees grow in earth, never
 * on stone. Cars and vans and buses stop at red; people cross on green.
 */

const T = 8
const ROAD = 4
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
  /** A building: light from outside stops at it. */
  solid?: boolean
}

export interface City {
  /** Colour of the ground at a point outside the building, in floor units. */
  ground(a: number, b: number): string
  things: CityThing[]
  /** Moves the traffic on; returns what moves, to be drawn this frame. */
  move(t: number, dt: number): CityThing[]
}

/** Street bands (start tile) along one axis: one round the lab's square, then at uneven gaps. */
function bands(lo: number, hi: number, size: number, r: () => number): number[] {
  const out: number[] = []
  for (let s = lo - ROAD; s > -40; s -= ROAD + 11 + Math.floor(r() * 13)) out.push(s)
  for (let s = hi; s < size + 40; s += ROAD + 11 + Math.floor(r() * 13)) out.push(s)
  return out.sort((x, y) => x - y)
}

const inBand = (list: number[], t: number) => list.find((s) => t >= s && t < s + ROAD)

/** Ground kinds, per tile. */
const G = { road: 1, pavement: 2, grass: 3, paving: 4, plot: 5, asphalt: 6, sand: 7 } as const

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

interface Mover {
  axis: Axis
  dir: 1 | -1
  at: number
  across: number
  speed: number
  len: number
  prop: Prop
  person?: { frames: Frames; walked: number }
}

const COATS = ['#3a5f9a', '#c2443e', '#e0b14a', '#4c9a7a', '#6c5a8a', '#2b2f36', '#d8d0c0', '#e07a5f', '#7a8a3a', '#5a3a2a']
const SKINS = ['#f1c9a5', '#e8b48f', '#8d5a3b', '#f6d7c3', '#c98e66', '#5e3b28']
const HAIRS = ['#1d1a22', '#5a3424', '#141018', '#c9a06a', '#2a2a33', '#8a4a2a', '#b8b0a8']
const PAINT = ['#e8ecf0', '#3a5f9a', '#5fd7e6', '#c2443e', '#2b2f36', '#7be3a8', '#e0b14a', '#8a8f96', '#5a3a6a', '#f2f2ee']

/** `seen` says whether a footprint (floor units) would show on the painted world: what would not is not made. */
export function makeCity(plan: Plan, seen: (a0: number, b0: number, a1: number, b1: number) => boolean = () => true): City {
  const [ba0, bb0, ba1, bb1] = plan.box
  const seed = Math.floor(hashOf(`${plan.box.join()}`, 3) * 1e6)
  const r = rand(seed)
  // The lab's square: four tiles round the building.
  const lot = [ba0 - 4, bb0 - 4, ba1 + 4, bb1 + 4]
  const alongA = bands(lot[1], lot[3], plan.h, r) // streets running along a, by their b
  const alongB = bands(lot[0], lot[2], plan.w, r) // streets running along b, by their a
  const NA = plan.w * T
  const NB = plan.h * T
  const OFF = 6
  const W = plan.w + 2 * OFF
  const H = plan.h + 2 * OFF
  // What each tile is, round the lab: so the ground and what stands on it agree.
  const kinds = new Uint8Array(W * H)
  const kindAt = (ta: number, tb: number) => {
    const i = (tb + OFF) * W + ta + OFF
    return ta + OFF < 0 || tb + OFF < 0 || ta + OFF >= W || tb + OFF >= H ? G.grass : kinds[i]
  }
  const set = (ta: number, tb: number, k: number) => {
    if (ta + OFF >= 0 && tb + OFF >= 0 && ta + OFF < W && tb + OFF < H) kinds[(tb + OFF) * W + ta + OFF] = k
  }
  for (let tb = -OFF; tb < H - OFF; tb++)
    for (let ta = -OFF; ta < W - OFF; ta++) {
      const road = inBand(alongA, tb) !== undefined || inBand(alongB, ta) !== undefined
      const pave = alongB.some((s) => ta === s - 1 || ta === s + ROAD) || alongA.some((s) => tb === s - 1 || tb === s + ROAD)
      set(ta, tb, road ? G.road : pave ? G.pavement : G.paving)
    }

  const things: CityThing[] = []
  // Trees, kept in a coarse grid so checking the space round a new one is quick.
  const near = new Map<number, [number, number][]>()
  const cell = (a: number, b: number) => Math.floor(a / 16) * 100003 + Math.floor(b / 16)
  const treeKinds = Array.from({ length: 40 }, (_, k) => tree(seed + k * 7919))
  const plantTree = (a: number, b: number, gap: number) => {
    if (kindAt(Math.floor(a / T), Math.floor(b / T)) !== G.grass) return
    if (kindAt(Math.floor((a + 6) / T), Math.floor((b + 6) / T)) !== G.grass) return
    if (!seen(a, b, a + 6, b + 6)) return
    for (let i = -1; i <= 1; i++) for (let j = -1; j <= 1; j++) for (const [x, y] of near.get(cell(a + i * 16, b + j * 16)) ?? []) if (Math.hypot(x - a, y - b) < gap) return
    const k = cell(a, b)
    near.set(k, [...(near.get(k) ?? []), [a, b]])
    things.push({ prop: treeKinds[Math.floor(r() * treeKinds.length)], a, b, flip: 1 })
  }

  // Blocks: the spaces between streets.
  const spans = (list: number[], lo: number, hi: number) => {
    const out: [number, number][] = []
    const all = [lo - 1 - ROAD, ...list, hi + 1]
    for (let i = 0; i < all.length - 1; i++) out.push([all[i] + ROAD + 1, all[i + 1] - 1])
    return out.filter(([x, y]) => y - x >= 3)
  }
  const labA = (lot[0] + lot[2]) / 2
  const labB = (lot[1] + lot[3]) / 2
  const labFront = (lot[2] + lot[3]) * T
  for (const [x0, x1] of spans(alongB, -OFF, W - OFF)) {
    for (const [y0, y1] of spans(alongA, -OFF, H - OFF)) {
      const mid = [(x0 + x1) / 2, (y0 + y1) / 2]
      if (!seen(x0 * T, y0 * T, x1 * T, y1 * T)) continue
      if (mid[0] > lot[0] && mid[0] < lot[2] && mid[1] > lot[1] && mid[1] < lot[3]) {
        labSquare(x0, y0, x1, y1)
        continue
      }
      const far = Math.hypot(mid[0] - labA, mid[1] - labB) / 20
      const front = (x0 + y0) * T > labFront - 40
      const h = r()
      const kind = far < 1.4 && h < 0.35 ? 'park' : h < 0.18 ? 'park' : h < 0.26 ? 'square' : 'built'
      if (kind === 'park') park(x0, y0, x1, y1)
      else if (kind === 'square') square(x0, y0, x1, y1)
      else built(x0, y0, x1, y1, far, front)
    }
  }

  function fill(x0: number, y0: number, x1: number, y1: number, k: number) {
    for (let b = y0; b < y1; b++) for (let a = x0; a < x1; a++) set(a, b, k)
  }

  function labSquare(x0: number, y0: number, x1: number, y1: number) {
    // Paving, with lawns of uneven shape here and there where trees grow; benches, bike racks,
    // planters on the stone.
    fill(x0, y0, x1, y1, G.paving)
    for (let b = y0; b < y1; b++)
      for (let a = x0; a < x1; a++) {
        const insideLab = a >= ba0 - 1 && a < ba1 + 1 && b >= bb0 - 1 && b < bb1 + 1
        if (!insideLab && noise(a / 4.5, b / 4.5, seed) > 0.56) set(a, b, G.grass)
      }
    for (let k = 0; k < 260; k++) plantTree((x0 + r() * (x1 - x0)) * T, (y0 + r() * (y1 - y0)) * T, 10)
    for (let k = 0; k < 10; k++) {
      const a = Math.floor(x0 + r() * (x1 - x0 - 3))
      const b = Math.floor(y0 + r() * (y1 - y0 - 2))
      const clear = [0, 1, 2].every((d) => kindAt(a + d, b) === G.paving && !(a + d >= ba0 - 1 && a + d < ba1 + 1 && b >= bb0 - 1 && b < bb1 + 1))
      if (!clear) continue
      const pick = r()
      things.push({ prop: pick < 0.4 ? bench() : pick < 0.65 ? bikeRack(k + seed) : planter(k + seed), a: a * T + 1, b: b * T + 2, flip: 1 })
    }
  }

  function park(x0: number, y0: number, x1: number, y1: number) {
    fill(x0, y0, x1, y1, G.grass)
    // A playground in a clearing, sand under it.
    const pw = Math.min(8, x1 - x0 - 2)
    const ph = Math.min(7, y1 - y0 - 2)
    const pa = x0 + 1 + Math.floor(r() * Math.max(1, x1 - x0 - pw - 2))
    const pb = y0 + 1 + Math.floor(r() * Math.max(1, y1 - y0 - ph - 2))
    const playground = pw >= 6 && ph >= 5
    if (playground) {
      fill(pa, pb, pa + pw, pb + ph, G.sand)
      things.push({ prop: slide(['#3fa86a', '#c2443e', '#3a5f9a'][Math.floor(r() * 3)]), a: pa * T + 2, b: pb * T + 2, flip: 1 })
      things.push({ prop: swings(), a: (pa + pw - 3) * T, b: pb * T + 2, flip: -1 })
      things.push({ prop: seesaw(), a: pa * T + 4, b: (pb + ph - 1) * T, flip: 1 })
      if (pw >= 7) things.push({ prop: sandpit(), a: (pa + 3) * T, b: (pb + 3) * T, flip: 1 })
      things.push({ prop: bench(), a: pa * T, b: (pb + ph) * T + 2, flip: 1 })
    }
    // A thick wood with clearings: trees close together, overlapping.
    for (let k = 0; k < 900; k++) {
      const a = (x0 + r() * (x1 - x0)) * T
      const b = (y0 + r() * (y1 - y0)) * T
      if (playground && a > (pa - 1) * T && a < (pa + pw + 1) * T && b > (pb - 1) * T && b < (pb + ph + 1) * T) continue
      if (noise(a / 30, b / 30, seed + 5) < 0.38) continue
      if (Math.abs(noise(a / 55, b / 55, seed) - 0.5) < 0.035) continue
      plantTree(a, b, 7 + r() * 4)
    }
  }

  function square(x0: number, y0: number, x1: number, y1: number) {
    fill(x0, y0, x1, y1, G.paving)
    const ca = Math.floor((x0 + x1) / 2) - 1
    const cb = Math.floor((y0 + y1) / 2) - 1
    things.push({ prop: fountain(), a: ca * T - 4, b: cb * T - 4, flip: 1 })
    // Lawns in the corners, with a tree or two.
    for (const [a, b] of [
      [x0, y0],
      [x1 - 3, y0],
      [x0, y1 - 3],
      [x1 - 3, y1 - 3],
    ]) {
      if (r() < 0.3) continue
      fill(a, b, a + 3, b + 3, G.grass)
      plantTree(a * T + 8 + r() * 6, b * T + 8 + r() * 6, 8)
    }
    for (let k = 0; k < 4; k++) things.push({ prop: k % 2 ? planter(seed + k) : bench(), a: (x0 + 1 + r() * (x1 - x0 - 4)) * T, b: (y0 + 1 + r() * (y1 - y0 - 3)) * T, flip: 1 })
  }

  function built(x0: number, y0: number, x1: number, y1: number, far: number, front: boolean) {
    fill(x0, y0, x1, y1, G.grass)
    // Plots of uneven size: split the block again and again, at uneven points.
    const plots: [number, number, number, number][] = []
    const split = (a0: number, b0: number, a1: number, b1: number, depth: number) => {
      const w = a1 - a0
      const h = b1 - b0
      if (depth > 3 || (w < 10 && h < 10) || (depth > 0 && r() < 0.25)) {
        plots.push([a0, b0, a1, b1])
        return
      }
      if (w >= h && w >= 8) {
        const at = a0 + Math.max(4, Math.min(w - 4, Math.round(w * (0.3 + r() * 0.4))))
        split(a0, b0, at, b1, depth + 1)
        split(at, b0, a1, b1, depth + 1)
      } else if (h >= 8) {
        const at = b0 + Math.max(4, Math.min(h - 4, Math.round(h * (0.3 + r() * 0.4))))
        split(a0, b0, a1, at, depth + 1)
        split(a0, at, a1, b1, depth + 1)
      } else plots.push([a0, b0, a1, b1])
    }
    split(x0, y0, x1, y1, 0)
    for (const [a0, b0, a1, b1] of plots) {
      const w = a1 - a0
      const h = b1 - b0
      const roll = r()
      if (roll < 0.1 || w < 3 || h < 3) {
        // A yard: grass and a tree or two.
        for (let k = 0; k < 3; k++) plantTree((a0 + r() * w) * T, (b0 + r() * h) * T, 10)
        continue
      }
      if (roll < 0.2 && w >= 5 && h >= 4) {
        // A car park.
        fill(a0, b0, a1, b1, G.asphalt)
        for (let a = a0; a + 2 <= a1; a += 2) {
          if (r() < 0.35) continue
          const make: Make = r() < 0.2 ? 'van' : r() < 0.5 ? 'small' : 'saloon'
          things.push({ prop: parked(make, PAINT[Math.floor(r() * PAINT.length)]), a: a * T + 2, b: b0 * T + 2, flip: -1 })
        }
        continue
      }
      fill(a0, b0, a1, b1, G.plot)
      // Not always the whole plot: a little back from the edge, by a random margin.
      const ma = r() < 0.5 ? 1 : 0
      const mb = r() < 0.5 ? 1 : 0
      const da = (w - ma - (r() < 0.4 ? 1 : 0)) * T - 2
      const db = (h - mb - (r() < 0.4 ? 1 : 0)) * T - 2
      if (da < 18 || db < 18) continue
      if (!seen((a0 + ma) * T, (b0 + mb) * T, (a0 + ma) * T + da, (b0 + mb) * T + db)) continue
      const small = da * db < 900
      const kind: Kind = front
        ? r() < 0.6
          ? 'shop'
          : 'warehouse'
        : small
          ? (['shop', 'home', 'home', 'office'] as Kind[])[Math.floor(r() * 4)]
          : (['office', 'glass', 'home', 'stepped', 'warehouse', 'office', 'home'] as Kind[])[Math.floor(r() * 7)]
      const near = far < 1.3
      const storeys = kind === 'shop' || kind === 'warehouse' ? 0 : front ? 0 : near ? 1 + Math.floor(r() * 2) : Math.min(7, 1 + Math.floor(r() * (2 + far * 1.6)))
      things.push({ prop: building(kind, da, db, storeys, Math.floor(r() * 1e6)), a: (a0 + ma) * T + 1, b: (b0 + mb) * T + 1, flip: 1, solid: true })
      // Round it, the rest of the plot is lawn: a tree may stand there.
      if (r() < 0.5) plantTree((a0 + w - 0.5) * T, (b0 + r() * h) * T, 10)
    }
  }

  const ground = (a: number, b: number): string => {
    const ta = Math.floor(a / T)
    const tb = Math.floor(b / T)
    const kind = kindAt(ta, tb)
    if (kind === G.road) {
      const sa = inBand(alongB, ta)
      const sb = inBand(alongA, tb)
      const asphalt = noise(a / 3, b / 3, 5) > 0.72 ? '#454a54' : noise(a / 9, b / 9, 6) > 0.7 ? '#40454e' : '#3e434c'
      if (sa !== undefined && sb !== undefined) return asphalt
      if (sb !== undefined) {
        if (alongB.some((s) => ta >= s - 2 && ta < s + ROAD + 2)) return Math.floor(b / 2.5) % 2 === 0 ? '#e9e6da' : asphalt
        if (Math.abs(b - (sb + ROAD / 2) * T) < 0.6 && Math.floor(a / 10) % 2 === 0) return '#e6dfb8'
        return asphalt
      }
      if (alongA.some((s) => tb >= s - 2 && tb < s + ROAD + 2)) return Math.floor(a / 2.5) % 2 === 0 ? '#e9e6da' : asphalt
      if (Math.abs(a - (sa! + ROAD / 2) * T) < 0.6 && Math.floor(b / 10) % 2 === 0) return '#e6dfb8'
      return asphalt
    }
    if (kind === G.pavement) {
      const kerb = alongB.some((s) => Math.abs(a - s * T) < 0.8 || Math.abs(a - (s + ROAD) * T) < 0.8) || alongA.some((s) => Math.abs(b - s * T) < 0.8 || Math.abs(b - (s + ROAD) * T) < 0.8)
      if (kerb) return '#9a968c'
      return a % 8 < 0.5 || b % 8 < 0.5 ? '#b4afa4' : noise(a / 4, b / 4, 2) > 0.7 ? '#c2bdb2' : '#c8c3b8'
    }
    if (kind === G.grass) {
      const n = noise(a / 9, b / 9, 13) * 0.9 + 0.55 * noise(a / 2.2, b / 2.2, 29) + 0.25 * noise(a / 0.9, b / 0.9, 31)
      if (Math.abs(noise(a / 55, b / 55, seed) - 0.5) < 0.03) return '#cdbf9e'
      const speck = noise(a * 1.7, b * 1.7, 43)
      if (speck > 0.955) return speck > 0.975 ? '#e8d77a' : '#e3a9c8'
      return n > 1.1 ? '#75a560' : n > 0.88 ? '#659552' : n > 0.62 ? '#578747' : '#4b793e'
    }
    if (kind === G.sand) return noise(a / 1.5, b / 1.5, 3) > 0.6 ? '#e2d0a2' : '#d8c494'
    if (kind === G.asphalt) return Math.abs((a % 16) - 0) < 0.7 ? '#e8e6dc' : noise(a / 3, b / 3, 7) > 0.68 ? '#4a4f58' : '#444851'
    if (kind === G.plot) return noise(a / 5, b / 5, 4) > 0.5 ? '#c9c4b8' : '#c2bdb1'
    const seam = a % 8 < 0.5 || b % 8 < 0.5
    return seam ? '#b9b3a8' : (ta * 7 + tb * 3) % 5 === 0 ? '#cbc6bb' : (ta + tb) % 2 ? '#d6d1c6' : '#d0cbc0'
  }

  // Street lamps on the pavements at uneven spacing; traffic lights at the crossings.
  for (const s of alongA)
    for (let a = -OFF + Math.floor(r() * 4); a < W - OFF; a += 6 + Math.floor(r() * 4))
      if (inBand(alongB, a) === undefined && inBand(alongB, a + 1) === undefined) things.push({ prop: streetLamp(), a: a * T + 4, b: (s + ROAD) * T + 3, flip: 1 })
  for (const sa of alongB)
    for (const sb of alongA) {
      things.push({ prop: trafficLight(), a: (sa - 1) * T + 3, b: (sb - 1) * T + 3, flip: 1 })
      things.push({ prop: trafficLight(), a: (sa + ROAD) * T + 3, b: (sb + ROAD) * T + 3, flip: 1 })
    }

  // Traffic: vehicles of all makes in both lanes of every street, people on both pavements.
  const cache = new Map<string, Prop>()
  const made = (make: Make, colour: string, view: 'front' | 'back', alongBAxis: boolean) => {
    const key = `${make}${colour}${view}${alongBAxis}`
    let p = cache.get(key)
    if (!p) {
      p = vehicle(make, colour, view)
      if (alongBAxis) p = mirrored(p)
      cache.set(key, p)
    }
    return p
  }
  const movers: Mover[] = []
  const lookFor = (): Look => ({
    skin: SKINS[Math.floor(r() * SKINS.length)],
    hair: HAIRS[Math.floor(r() * HAIRS.length)],
    style: (['short', 'long', 'curly', 'bun', 'pony'] as const)[Math.floor(r() * 5)],
    shirt: COATS[Math.floor(r() * COATS.length)],
    coat: COATS[Math.floor(r() * COATS.length)],
    trousers: ['#2e3644', '#5a5f6a', '#3a4a6a', '#6a5a4a'][Math.floor(r() * 4)],
    glasses: r() > 0.8,
  })
  for (const [axis, list, size] of [
    ['a', alongA, NA],
    ['b', alongB, NB],
  ] as [Axis, number[], number][]) {
    for (const s of list) {
      for (const dir of [1, -1] as const) {
        const n = 1 + Math.floor(r() * 3)
        for (let i = 0; i < n; i++) {
          const roll = r()
          const make: Make = roll < 0.08 ? 'bus' : roll < 0.25 ? 'van' : roll < 0.55 ? 'small' : 'saloon'
          const prop = made(make, make === 'bus' ? '#3a8a7a' : PAINT[Math.floor(r() * PAINT.length)], dir === 1 ? 'front' : 'back', axis === 'b')
          const across = s * T + (dir === 1 ? 17 : 1)
          const len = axis === 'a' ? prop.da : prop.db
          movers.push({ axis, dir, at: r() * size, across, speed: make === 'bus' ? 22 : 28 + r() * 16, len, prop })
        }
        const walkAcross = dir === 1 ? (s + ROAD) * T + 2 + r() * 4 : s * T - 2 - r() * 4
        const people = Math.floor(r() * 3)
        for (let i = 0; i < people; i++) {
          movers.push({
            axis,
            dir: r() > 0.5 ? 1 : -1,
            at: r() * size,
            across: walkAcross,
            speed: 7 + r() * 5,
            len: 4,
            prop: { da: 2, db: 2, sprite: { canvas: document.createElement('canvas'), ax: 0, ay: 0 } },
            person: { frames: scientistFrames(lookFor()), walked: 0 },
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
        const light = lightFor(m.axis, t)
        const gap = m.person ? 6 : 18
        let step = m.speed * dt
        if (light !== 'green') {
          const nose = m.dir === 1 ? m.at + m.len : m.at
          for (const [c0, c1] of crossings(m.axis)) {
            const line = m.dir === 1 ? c0 - gap : c1 + gap
            const ahead = (line - nose) * m.dir
            if (ahead >= -0.5 && ahead < step + (light === 'amber' ? 6 : 14)) step = Math.max(0, Math.min(step, ahead - 0.5))
          }
        }
        if (!m.person) {
          for (const o of movers) {
            if (o === m || o.person || o.axis !== m.axis || o.across !== m.across) continue
            const d = (o.at - m.at) * m.dir
            const need = (m.dir === 1 ? m.len : o.len) + 8
            if (d > 0 && d < need + step) step = Math.max(0, Math.min(step, d - need))
          }
        }
        m.at += step * m.dir
        if (m.at > size + 120) m.at = -120
        if (m.at < -120) m.at = size + 120
        const a = m.axis === 'a' ? m.at : m.across
        const b = m.axis === 'a' ? m.across : m.at
        if (m.person) {
          m.person.walked += step / m.speed
          const facing: Facing = m.axis === 'a' ? (m.dir === 1 ? 'SE' : 'NW') : m.dir === 1 ? 'SW' : 'NE'
          const canvas = m.person.frames.get(facing, step > 0.01 ? 'walk' : 'stand', Math.floor(m.person.walked * 8))
          out.push({ prop: { da: 2, db: 2, sprite: { canvas, ax: 16, ay: 55 } }, a: a - 1, b: b - 1, flip: 1 })
          continue
        }
        out.push({ prop: m.prop, a, b, flip: m.axis === 'b' ? -1 : 1 })
      }
      return out
    },
  }
}

const parkedCache = new Map<string, Prop>()
function parked(make: Make, colour: string): Prop {
  const key = make + colour
  let p = parkedCache.get(key)
  if (!p) {
    p = mirrored(vehicle(make, colour, 'parked'))
    parkedCache.set(key, p)
  }
  return p
}
