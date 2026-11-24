import { box, YF, type Sprite } from '../iso'
import { benchDesk, capsule, chair, coffeeCounter, cornerDesk, holoTable, microscopeBench, mirrored, plant, rack, sofa, totem, whiteboard, type BoardKind, type Prop } from '../props'
import { mix, noise, Painter, rand, toCanvas } from '../px'
import { planLab, type Furnished, type Item } from './furnish'
import { hashOf, spaceAt, wallBetween, type OfficeSpec, type Plan, type Space } from './layout'
import { bench, car, streetLamp, tree } from './outdoor'

/**
 * Everything about the lab that does not move, made from the offices: the ground round the
 * building (grass, a paved square, a road with a pavement), the floors, the walls (full height
 * at the back of the building, with windows; cut low everywhere else, so you see in), the
 * furniture, the trees and lamps; and where the lights are.
 */

export const T = 8
export const WALL_H = 58
export const LOW = 8
const PAD = 12

export interface Placed {
  prop: Prop
  a: number
  b: number
  flip: number
  /** Indoors or out: outdoor things lose their light at night. */
  outdoor: boolean
  room: number
}

export interface WallPiece {
  sprite: Sprite
  a0: number
  b0: number
  a1: number
  b1: number
  window: boolean
}

export interface Pool {
  a: number
  b: number
  r: number
  color: string
  strength: number
  kind: 'ceiling' | 'street' | 'thing' | 'shiori'
}

export interface World {
  plan: Plan
  furnished: Furnished
  width: number
  height: number
  screen(a: number, b: number, z?: number): [number, number]
  ground: HTMLCanvasElement
  /** White where the building is (floors and its tall walls): indoor light goes there. */
  indoor: HTMLCanvasElement
  props: Placed[]
  walls: WallPiece[]
  pools: Pool[]
  /** The middle of Shiori's hall, in floor units. */
  centre: [number, number]
  roadB: number
}

const cache = new Map<string, Prop>()
function propFor(item: Item): Prop {
  const key = `${item.kind}|${item.accent}|${item.variant ?? ''}|${item.seed % 3}|${item.flip}`
  let prop = cache.get(key)
  if (!prop) {
    const s = item.seed % 3
    const liquids = ['#37d6a0', '#4cc3ff', '#b98cff']
    switch (item.kind) {
      case 'bench':
        prop = benchDesk(item.accent, s + 1)
        break
      case 'corner':
        prop = cornerDesk(item.accent, s + 2)
        break
      case 'holo':
        prop = holoTable(item.accent)
        break
      case 'microscope':
        prop = microscopeBench(item.accent)
        break
      case 'capsule':
        prop = capsule(liquids[s], s * 5 + 3)
        break
      case 'rack':
        prop = rack(s * 5 + 4)
        break
      case 'board':
        prop = whiteboard((item.variant ?? 'math') as BoardKind, s + 5)
        break
      case 'coffee':
        prop = coffeeCounter(item.accent)
        break
      case 'sofa':
        prop = sofa('#6c7fa8')
        break
      case 'plant':
        prop = plant(s + 6)
        break
      case 'chair':
        prop = chair(item.accent)
        break
      case 'totem':
        prop = totem(item.accent)
        break
    }
    if (item.flip) prop = mirrored(prop)
    cache.set(key, prop)
  }
  return prop
}

/** A wall piece one tile long: along a (`a`) or along b (`b`), full or low, with a window or not. */
function wallSprite(dir: 'a' | 'b', full: boolean, window: boolean): Sprite {
  const h = full ? WALL_H : LOW
  const w = 2 * (T + 2) + 10
  const top = h + 4
  const p = new Painter(w, top + T * 2 + 6)
  const ax = dir === 'a' ? 6 : w - 8
  const ay = top
  const at = (a: number, b: number, z = 0): [number, number] => [ax + (a - b) * 2, ay + (a + b) * YF - z]
  const [da, db] = dir === 'a' ? [T, 2] : [2, T]
  const panel = full ? '#d3dbe3' : '#dfe5eb'
  box(p, at, 0, 0, 0, da, db, h, { top: '#f1f4f7', left: panel, right: mix(panel, '#0b1020', 0.18) })
  if (full) {
    // A skirting with a cold light strip, a seam on top.
    for (let u = 0; u < 2 * (dir === 'a' ? da : db); u++) {
      for (let v = 0; v < 4; v++) {
        const [x, y] = dir === 'a' ? at(u / 2, db, v) : at(da, u / 2, v)
        p.set(x, y, v === 3 ? '#62d6e8' : '#4a5466')
      }
    }
    if (window) {
      // Glass: dark in the drawing; the sky is painted into it as light, live.
      for (let u = 2; u < 2 * T - 2; u++) {
        for (let v = 18; v < h - 8; v++) {
          const [x, y] = dir === 'a' ? at(u / 2, db, v) : at(da, u / 2, v)
          const frame = u === 2 || u === 2 * T - 3 || v === 18 || v === h - 9
          p.set(x, y, frame ? '#8d99a8' : '#1c2533')
          if (!frame) {
            p.emitting = true
            p.set(x, y, '#ffffff')
            p.emitting = false
            p.set(x, y, '#1c2533')
          }
        }
      }
    }
  }
  p.outline('#18202b')
  return { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined }
}

const wallSprites = new Map<string, Sprite>()
function wallFor(dir: 'a' | 'b', full: boolean, window: boolean): Sprite {
  const key = `${dir}${full}${window}`
  let s = wallSprites.get(key)
  if (!s) {
    s = wallSprite(dir, full, window)
    wallSprites.set(key, s)
  }
  return s
}

function floorOf(space: Space, a: number, b: number, centre: [number, number]): string {
  const ta = Math.floor(a / T)
  const tb = Math.floor(b / T)
  const fa = a - ta * T
  const fb = b - tb * T
  const seam = fa < 0.5 || fb < 0.5
  const edge = a - space.a0 * T < 1.2 || b - space.b0 * T < 1.2 || space.a1 * T - a < 1.2 || space.b1 * T - b < 1.2
  let c: string
  switch (space.kind) {
    case 'hall': {
      const d = Math.hypot(a - centre[0], b - centre[1])
      c = (ta + tb) % 2 ? '#cbd3dc' : '#c6ced8'
      if (d < 40) {
        c = d > 38.5 ? '#79e3f2' : d > 37 ? '#aab6c3' : Math.floor(d / 6) % 2 ? '#c2cbd5' : '#c9d1da'
        const ang = Math.atan2(b - centre[1], a - centre[0])
        if (d < 37 && Math.abs(((((ang / (Math.PI / 6)) % 1) + 1) % 1) - 0.5) > 0.47) c = '#b0bac6'
      }
      break
    }
    case 'corridor': {
      c = '#c0c8d2'
      const mid = space.a1 - space.a0 <= 2 ? Math.abs(a - (space.a0 + space.a1) * 4) : Math.abs(b - (space.b0 + space.b1) * 4)
      if (mid < 0.7 && (space.a1 - space.a0 <= 2 || space.b1 - space.b0 <= 2)) c = '#7fd8e6'
      break
    }
    case 'lounge': {
      const d = Math.hypot(a - (space.a0 + space.a1) * 4, b - (space.b0 + space.b1) * 4)
      c = d < 22 ? (d > 20.5 ? mix(space.accent, '#ffffff', 0.3) : mix('#8a97a8', space.accent, 0.18 + 0.05 * noise(a / 3, b / 3))) : (ta + tb) % 2 ? '#d9d4e2' : '#d4cfdd'
      if (d < 22) return c
      break
    }
    default: {
      const base = mix('#d6dde4', space.accent, 0.1)
      c = (ta + tb) % 2 ? base : mix(base, '#9aa6b4', 0.06)
      if (space.flavor === 'code' && ((Math.abs(fa - 2) < 0.6 && Math.abs(fb - 2) < 0.6) || (Math.abs(fa - 6) < 0.6 && Math.abs(fb - 6) < 0.6))) c = mix(c, '#4a5466', 0.3)
      if (space.flavor === 'research' && (ta + tb) % 3 === 0 && fa > 1.5 && fa < 6.5 && fb > 1.5 && fb < 6.5 && Math.floor(fa * 2) % 2 === 0) c = mix(c, '#3a4352', 0.4)
      if (space.flavor === 'creative') c = mix(c, '#c9a77c', 0.18 + 0.06 * noise(a / 2, b / 14, 3))
    }
  }
  if (edge && space.kind !== 'hall' && space.kind !== 'corridor') return mix(space.accent, '#ffffff', 0.2)
  if (seam) c = mix(c, '#7d8999', 0.2)
  return c
}

export function buildWorld(offices: OfficeSpec[]): World {
  const { plan, furnished } = planLab(offices)
  const NA = plan.w * T
  const NB = plan.h * T
  const OX = 2 * NB + PAD
  const OY = WALL_H + PAD + 6
  const width = 2 * (NA + NB) + 2 * PAD
  const height = Math.ceil(OY + (NA + NB) * YF + PAD)
  const screen = (a: number, b: number, z = 0): [number, number] => [OX + (a - b) * 2, OY + (a + b) * YF - z]
  const hall = plan.hall
  const centre: [number, number] = [((hall.a0 + hall.a1) / 2) * T, ((hall.b0 + hall.b1) / 2) * T]

  // How far each tile is from the building, for the paved square round it.
  const dist = new Float32Array(plan.w * plan.h).fill(99)
  for (let b = 0; b < plan.h; b++)
    for (let a = 0; a < plan.w; a++) {
      if (plan.cells[b * plan.w + a] < 0) continue
      for (let y = Math.max(0, b - 3); y <= Math.min(plan.h - 1, b + 3); y++)
        for (let x = Math.max(0, a - 3); x <= Math.min(plan.w - 1, a + 3); x++) dist[y * plan.w + x] = Math.min(dist[y * plan.w + x], Math.max(Math.abs(x - a), Math.abs(y - b)))
    }
  const roadB = (plan.h - 3) * T
  const walkB = roadB - 7
  const pathA = centre[0]
  const parkA = pathA + 40

  const ground = (a: number, b: number): string => {
    const ta = Math.floor(a / T)
    const tb = Math.floor(b / T)
    if (b >= roadB && b < roadB + 18) {
      const mid = roadB + 9
      if (Math.abs(b - mid) < 0.6 && Math.floor(a / 10) % 2 === 0) return '#e6dfb8'
      if (b < roadB + 1) return '#8d8a84'
      return noise(a / 3, b / 3, 5) > 0.7 ? '#3e434c' : '#383d46'
    }
    if (b >= walkB && b < roadB) return a % 8 < 0.6 ? '#8f8a81' : '#a8a398'
    const d = ta >= 0 && tb >= 0 && ta < plan.w && tb < plan.h ? dist[tb * plan.w + ta] : 99
    const path = Math.abs(a - pathA) < 9 && b > centre[1] && b < walkB
    if (d <= 2 || path) {
      const seam = a % 8 < 0.5 || b % 8 < 0.5
      return seam ? '#a9a296' : (ta + tb) % 2 ? '#c3bcb0' : '#bdb6aa'
    }
    if (d <= 2.6) return '#8e8a80'
    // The car park, by the road.
    if (b >= walkB - 26 && b < walkB && a >= parkA && a < parkA + 80) {
      if (b < walkB - 25) return '#e8e6dc'
      if ((a - parkA) % 16 < 0.7) return '#e8e6dc'
      return noise(a / 3, b / 3, 7) > 0.68 ? '#4a4f58' : '#444851'
    }
    // Grass: fine blades in a few greens, slow patches of lighter and darker, the odd flower.
    const n = noise(a / 9, b / 9, 13) * 0.9 + 0.55 * noise(a / 2.2, b / 2.2, 29) + 0.3 * noise(a / 0.8, b / 0.8, 31)
    const speck = noise(a * 1.7, b * 1.7, 43)
    if (speck > 0.94) return speck > 0.97 ? '#e8d77a' : '#e3a9c8'
    return n > 1.15 ? '#75a560' : n > 0.95 ? '#659552' : n > 0.75 ? '#578747' : n > 0.55 ? '#4b793e' : '#406b36'
  }

  const p = new Painter(width, height)
  const mask = new Painter(width, height)
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const dx = x + 0.5 - OX
      const dy = y + 0.5 - OY
      const a = (dx / 2 + dy / YF) / 2
      const b = (dy / YF - dx / 2) / 2
      const space = spaceAt(plan, Math.floor(a / T), Math.floor(b / T))
      if (space && a >= 0 && b >= 0) {
        p.set(x, y, floorOf(space, a, b, centre))
        mask.set(x, y, '#ffffff')
      } else p.set(x, y, ground(a, b))
    }
  }

  // Walls: along every tile edge between two spaces (or a space and the outside), except doors.
  const walls: WallPiece[] = []
  for (let b = 0; b < plan.h; b++) {
    for (let a = 0; a < plan.w; a++) {
      for (const dir of ['a', 'b'] as const) {
        if (!wallBetween(plan, a, b, dir)) continue
        const here = spaceAt(plan, a, b)
        const there = spaceAt(plan, dir === 'a' ? a + 1 : a, dir === 'b' ? b + 1 : b)
        // Full height only on the building's back skin: outside behind, inside in front, and
        // nothing of the building further back (a notch between wings stays open to the eye).
        let full = !here && !!there
        if (full) {
          for (let k = (dir === 'b' ? b : a) - 1; k >= 0 && full; k--) if ((dir === 'b' ? spaceAt(plan, a, k) : spaceAt(plan, k, b)) !== null) full = false
        }
        if (dir === 'b') {
          const window = full && a % 3 !== 0
          walls.push({ sprite: wallFor('a', full, window), a0: a * T, b0: (b + 1) * T - 1, a1: (a + 1) * T, b1: (b + 1) * T + 1, window })
        } else {
          const window = full && b % 3 !== 0
          walls.push({ sprite: wallFor('b', full, window), a0: (a + 1) * T - 1, b0: b * T, a1: (a + 1) * T + 1, b1: (b + 1) * T, window })
        }
      }
    }
  }
  // The tall walls are indoors too, for the light.
  const indoor = mask.canvas()
  const ictx = indoor.getContext('2d')!
  for (const w of walls) {
    if (w.sprite.canvas.height < WALL_H) continue
    const [x, y] = screen(w.a0, w.b0)
    ictx.drawImage(w.sprite.canvas, x - w.sprite.ax, y - w.sprite.ay)
  }
  ictx.globalCompositeOperation = 'source-in'
  ictx.fillStyle = '#ffffff'
  ictx.fillRect(0, 0, width, height)

  // Furniture.
  const props: Placed[] = furnished.items.map((item) => ({ prop: propFor(item), a: item.a, b: item.b, flip: item.flip ? -1 : 1, outdoor: false, room: item.room }))
  // Outside: trees on the grass, lamps along the pavement and round the square, benches.
  const rnd = rand(Math.floor(hashOf(offices.map((o) => o.name).join(), 4) * 1e6))
  const taken: [number, number][] = []
  // Trees: a wood that thickens away from the building, out past the edges of the plan, so the
  // lab stands in a place and not in a void.
  const reach = 14 * T
  for (let k = 0; k < 9000; k++) {
    const a = -reach + rnd() * (NA + 2 * reach)
    const b = -reach + rnd() * (NB + 2 * reach)
    const [x, y] = screen(a, b)
    if (x < -20 || x > width + 20 || y < 20 || y > height + 40) continue
    if (b + 8 >= walkB - 2 && b < roadB + 24) continue
    if (b >= walkB - 30 && b < walkB && a >= parkA - 6 && a < parkA + 86) continue
    if (Math.abs(a - pathA) < 16 && b > centre[1] && b < walkB) continue
    const ta = Math.floor(a / T)
    const tb = Math.floor(b / T)
    const d = ta >= 0 && tb >= 0 && ta < plan.w && tb < plan.h ? dist[tb * plan.w + ta] : 99
    if (d < 4.5) continue
    // Sparse near the square, a wood further out.
    const far = Math.min(1, (Math.min(d, 20) - 4) / 9)
    const gap = 26 - far * 14
    if (rnd() > 0.25 + far * 0.75) continue
    if (taken.some(([px, py]) => Math.hypot(px - a, py - b) < gap)) continue
    taken.push([a, b])
    props.push({ prop: tree(Math.floor(rnd() * 1e6)), a, b, flip: 1, outdoor: true, room: -1 })
  }
  const colours = ['#c2443e', '#e8e4dc', '#3a5f9a', '#2b2f36', '#d8a23a']
  for (let k = 0; k < 5; k++)
    if (rnd() > 0.3) props.push({ prop: mirrored(car(colours[Math.floor(rnd() * colours.length)], false)), a: parkA + 4 + k * 16, b: walkB - 23, flip: -1, outdoor: true, room: -1 })
  for (let a = 4; a < NA; a += 7 * T) props.push({ prop: streetLamp(), a, b: walkB + 2, flip: 1, outdoor: true, room: -1 })
  props.push({ prop: bench(), a: pathA + 14, b: walkB - 10, flip: 1, outdoor: true, room: -1 })
  props.push({ prop: bench(), a: pathA - 30, b: walkB - 10, flip: 1, outdoor: true, room: -1 })

  // Lights: lamps on the ceiling of every space (more in offices, fewer in corridors), what
  // the furniture throws, Shiori's well, the street lamps.
  const pools: Pool[] = []
  for (const s of plan.spaces) {
    const step = 3
    // Each office a little different; the lounge warm and soft, corridors and the hall dimmer.
    const strength = s.kind === 'office' ? 0.3 + hashOf(s.name, 5) * 0.18 : s.kind === 'lounge' ? 0.26 : s.kind === 'corridor' ? 0.16 : 0.1
    const colour = s.kind === 'lounge' ? '#fff0dc' : s.kind === 'office' ? mix('#eef3ff', s.accent, 0.08) : '#e8f0ff'
    for (let b = s.b0 + 1; b < s.b1; b += step) for (let a = s.a0 + 1; a < s.a1; a += step) pools.push({ a: a * T + 4, b: b * T + 4, r: 34, color: colour, strength, kind: 'ceiling' })
  }
  pools.push({ a: centre[0], b: centre[1], r: 150, color: '#46ffb0', strength: 0.75, kind: 'shiori' })

  return { plan, furnished, width, height, screen, ground: p.canvas(), indoor, props, walls, pools, centre, roadB }
}

/** A sprite's light layer tinted: the windows' white becomes the sky. Cached per colour. */
const tinted = new Map<string, HTMLCanvasElement>()
export function tint(glow: HTMLCanvasElement, colour: string, key: string): HTMLCanvasElement {
  const k = `${key}|${colour}`
  let c = tinted.get(k)
  if (!c) {
    c = document.createElement('canvas')
    c.width = glow.width
    c.height = glow.height
    const ctx = c.getContext('2d')!
    ctx.drawImage(glow, 0, 0)
    ctx.globalCompositeOperation = 'multiply'
    ctx.fillStyle = colour
    ctx.fillRect(0, 0, c.width, c.height)
    ctx.globalCompositeOperation = 'destination-in'
    ctx.drawImage(glow, 0, 0)
    if (tinted.size > 400) tinted.clear()
    tinted.set(k, c)
  }
  return c
}

export { toCanvas }
