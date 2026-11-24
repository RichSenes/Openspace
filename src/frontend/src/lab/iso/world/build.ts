import { box, YF, type Sprite } from '../iso'
import { capsule, chair, coffeeCounter, holoTable, mirrored, plant, rack, sofa, totem, whiteboard, type BoardKind, type Prop } from '../props'
import { mix, noise, Painter, toCanvas } from '../px'
import { makeCity, type City } from './city'
import { decor, type DecorKind } from './decor'
import { arcade, beanbag, foosball, pingPong, vending } from './recreation'
import { benchFacing, cornerFacing, microscopeFacing } from './desks'
import { planLab, type Furnished, type Item } from './furnish'
import { edgeKind, hashOf, spaceAt, type OfficeSpec, type Plan, type Space } from './layout'

/**
 * Everything about the lab that does not move, made from the offices and fitted to the screen:
 * the lab in the middle, always; the floors, its walls (tall with windows only on its back skin,
 * low everywhere else so you see in), glass partitions between areas, the furniture; round it
 * the city, as far as the screen reaches; and where the lights are.
 */

export const T = 8
export const WALL_H = 58
export const LOW = 8

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
  city: City
  /** The canvas size: the screen, divided by the zoom. */
  width: number
  height: number
  screen(a: number, b: number, z?: number): [number, number]
  ground: HTMLCanvasElement
  /** White where the building is (floors and its tall walls): indoor light goes there. */
  indoor: HTMLCanvasElement
  /** White where the lab and the city's buildings stand: light from outside does not go through them. */
  solid: HTMLCanvasElement
  props: Placed[]
  walls: WallPiece[]
  pools: Pool[]
  /** The middle of Shiori's area, in floor units. */
  centre: [number, number]
}

const cache = new Map<string, Prop>()
function propFor(item: Item): Prop {
  const key = `${item.kind}|${item.accent}|${item.variant ?? ''}|${item.seed % (item.kind === 'decor' ? 6 : 3)}|${item.flip}`
  let prop = cache.get(key)
  if (!prop) {
    const s = item.seed % 3
    const liquids = ['#37d6a0', '#4cc3ff', '#b98cff']
    switch (item.kind) {
      case 'pingpong':
        prop = pingPong()
        break
      case 'foosball':
        prop = foosball()
        break
      case 'arcade':
        prop = arcade(item.seed)
        break
      case 'vending':
        prop = vending()
        break
      case 'beanbag':
        prop = beanbag(item.seed)
        break
      case 'decor':
        prop = decor((item.variant ?? 'bin') as DecorKind, item.accent, item.seed)
        break
      case 'bench':
        prop = benchFacing(item.accent, s + 1)
        break
      case 'corner':
        prop = cornerFacing(item.accent, s + 2)
        break
      case 'holo':
        prop = holoTable(item.accent)
        break
      case 'microscope':
        prop = microscopeFacing(item.accent)
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

/** A glass partition one tile long: a low frame and a faint pane, a shine across it. */
function glassSprite(dir: 'a' | 'b'): Sprite {
  const h = 20
  const w = 2 * (T + 2) + 10
  const top = h + 4
  const p = new Painter(w, top + T * 2 + 6)
  const ax = dir === 'a' ? 6 : w - 8
  const ay = top
  const at = (a: number, b: number, z = 0): [number, number] => [ax + (a - b) * 2, ay + (a + b) * YF - z]
  const [da, db] = dir === 'a' ? [T, 1] : [1, T]
  box(p, at, 0, 0, 0, da, db, 3, { top: '#c9d1da', left: '#8d99a8', right: '#77838f' })
  for (let u = 0; u < 2 * T; u++) {
    for (let v = 3; v < h - 1; v++) {
      const [x, y] = dir === 'a' ? at(u / 2, db, v) : at(da, u / 2, v)
      const shine = (u + v) % 19 === 0 || (u + v) % 19 === 1
      p.set(x, y, shine ? '#ffffff' : '#bfe6f5', shine ? 0.45 : 0.16)
    }
  }
  box(p, at, 0, 0, h - 1, da, db, 1, { top: '#e9eef2', left: '#a9b4c0', right: '#93a0ad' })
  const ends = dir === 'a' ? [0, T - 1] : [0, T - 1]
  for (const e of ends) box(p, at, dir === 'a' ? e : 0, dir === 'a' ? 0 : e, 0, dir === 'a' ? 1 : da, dir === 'a' ? db : 1, h, { top: '#e9eef2', left: '#a9b4c0', right: '#93a0ad' }, false)
  p.outline('#18202b', 200)
  return { canvas: p.canvas(), ax, ay }
}

const wallSprites = new Map<string, Sprite>()
function wallFor(dir: 'a' | 'b', kind: 'full' | 'low' | 'glass', window: boolean): Sprite {
  const key = `${dir}${kind}${window}`
  let s = wallSprites.get(key)
  if (!s) {
    s = kind === 'glass' ? glassSprite(dir) : wallSprite(dir, kind === 'full', window)
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
      // Her area: a wide floor of rings round the platform.
      if (d >= 40 && d < 62 && Math.floor(d) % 11 === 0) c = '#b8c2cd'
      if (d >= 40 && d < 62 && Math.floor(d) % 11 === 0 && Math.floor(Math.atan2(b - centre[1], a - centre[0]) * 8) % 3 === 0) c = '#7fd8e6'
      if (d < 40) {
        c = d > 38.5 ? '#79e3f2' : d > 37 ? '#aab6c3' : Math.floor(d / 6) % 2 ? '#c2cbd5' : '#c9d1da'
        const ang = Math.atan2(b - centre[1], a - centre[0])
        if (d < 37 && Math.abs(((((ang / (Math.PI / 6)) % 1) + 1) % 1) - 0.5) > 0.47) c = '#b0bac6'
      }
      break
    }
    case 'floor': {
      // The walkway: a darker, plainer floor, a guide light along its middle.
      c = (ta + tb) % 2 ? '#c3cbd5' : '#bec6d0'
      break
    }
    case 'games': {
      const n = noise(a / 2, b / 2, 8)
      c = (ta + tb) % 2 ? mix('#d8cfc4', space.accent, 0.12) : mix('#d2c9be', space.accent, 0.1)
      if (n > 0.8) c = mix(c, '#ffffff', 0.1)
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
  if (edge && space.kind !== 'hall' && space.kind !== 'floor') return mix(space.accent, '#ffffff', 0.2)
  if (seam) c = mix(c, '#7d8999', 0.2)
  return c
}

export function buildWorld(offices: OfficeSpec[], viewW: number, viewH: number): World {
  // The lab in the middle of the screen; the ground round it reaches every corner of it.
  let margin = 8
  for (;;) {
    const { plan } = planLab(offices, margin)
    const { screen } = placing(plan, viewW, viewH)
    const corners = [
      [0, 0],
      [viewW, 0],
      [0, viewH + 60],
      [viewW, viewH + 60],
    ]
    const ok = corners.every(([x, y]) => {
      const [a, b] = unscreen(screen, x, y)
      return a >= 2 * T && b >= 2 * T && a < (plan.w - 2) * T && b < (plan.h - 2) * T
    })
    if (ok || margin > 160) break
    margin += 6
  }
  const { plan, furnished } = planLab(offices, margin)
  const { screen } = placing(plan, viewW, viewH)
  const width = viewW
  const height = viewH
  const hall = plan.hall
  const centre: [number, number] = [((hall.a0 + hall.a1) / 2) * T, ((hall.b0 + hall.b1) / 2) * T]
  // Only what would show on the painted world is made.
  const seen = (a0: number, b0: number, a1: number, b1: number) => {
    const xs = [screen(a0, b0)[0], screen(a1, b0)[0], screen(a0, b1)[0], screen(a1, b1)[0]]
    const ys = [screen(a0, b0)[1], screen(a1, b1)[1]]
    return Math.max(...xs) > -40 && Math.min(...xs) < width + 40 && Math.max(...ys) > -40 && Math.min(...ys) - 240 < height + 40
  }
  const city = makeCity(plan, seen)

  const p = new Painter(width, height)
  const mask = new Painter(width, height)
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const [a, b] = unscreen(screen, x + 0.5, y + 0.5)
      const space = spaceAt(plan, Math.floor(a / T), Math.floor(b / T))
      if (space && a >= 0 && b >= 0) {
        p.set(x, y, floorOf(space, a, b, centre))
        mask.set(x, y, '#ffffff')
      } else p.set(x, y, city.ground(a, b))
    }
  }

  // Walls and partitions along every tile edge that has one.
  const walls: WallPiece[] = []
  for (let b = 0; b < plan.h; b++) {
    for (let a = 0; a < plan.w; a++) {
      for (const dir of ['a', 'b'] as const) {
        const kind = edgeKind(plan, a, b, dir)
        if (kind === 'none') continue
        let look: 'full' | 'low' | 'glass' = 'glass'
        if (kind === 'wall') {
          const here = spaceAt(plan, a, b)
          // Tall only on the building's back skin: outside behind, inside in front, and
          // nothing of the building further back.
          let full = !here
          for (let k = (dir === 'b' ? b : a) - 1; k >= 0 && full; k--) if ((dir === 'b' ? spaceAt(plan, a, k) : spaceAt(plan, k, b)) !== null) full = false
          look = full ? 'full' : 'low'
        }
        const window = look === 'full' && (dir === 'b' ? a : b) % 3 !== 0
        if (dir === 'b') walls.push({ sprite: wallFor('a', look, window), a0: a * T, b0: (b + 1) * T - (look === 'glass' ? 0.5 : 1), a1: (a + 1) * T, b1: (b + 1) * T + 1, window })
        else walls.push({ sprite: wallFor('b', look, window), a0: (a + 1) * T - (look === 'glass' ? 0.5 : 1), b0: b * T, a1: (a + 1) * T + 1, b1: (b + 1) * T, window })
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

  const props: Placed[] = furnished.items.map((item) => ({ prop: propFor(item), a: item.a, b: item.b, flip: item.flip ? -1 : 1, outdoor: false, room: item.room }))
  for (const t of city.things) props.push({ prop: t.prop, a: t.a, b: t.b, flip: t.flip, outdoor: true, room: -1 })
  // What stops the street's light: the lab and the buildings, their whole shapes.
  const solid = document.createElement('canvas')
  solid.width = width
  solid.height = height
  const sctx = solid.getContext('2d')!
  sctx.drawImage(indoor, 0, 0)
  for (const w of walls) {
    const [x, y] = screen(w.a0, w.b0)
    sctx.drawImage(w.sprite.canvas, x - w.sprite.ax, y - w.sprite.ay)
  }
  for (const t of city.things) {
    if (!t.solid) continue
    const [x, y] = screen(t.a, t.b)
    sctx.drawImage(t.prop.sprite.canvas, x - t.prop.sprite.ax, y - t.prop.sprite.ay)
  }
  sctx.globalCompositeOperation = 'source-in'
  sctx.fillStyle = '#ffffff'
  sctx.fillRect(0, 0, width, height)

  // Lamps on the ceiling of every area (each office a little different, the lounge warm and
  // soft, the walkway and her area dimmer), and the light of her well.
  const pools: Pool[] = []
  for (const s of plan.spaces) {
    const strength = s.kind === 'office' ? 0.3 + hashOf(s.name, 5) * 0.18 : s.kind === 'lounge' || s.kind === 'games' ? 0.26 : s.kind === 'floor' ? 0.16 : 0.08
    const colour = s.kind === 'lounge' || s.kind === 'games' ? '#fff0dc' : s.kind === 'office' ? mix('#eef3ff', s.accent, 0.08) : '#e8f0ff'
    for (let b = s.b0 + 1; b < s.b1; b += 3)
      for (let a = s.a0 + 1; a < s.a1; a += 3) if (spaceAt(plan, a, b)?.id === s.id) pools.push({ a: a * T + 4, b: b * T + 4, r: 34, color: colour, strength, kind: 'ceiling' })
  }
  pools.push({ a: centre[0], b: centre[1], r: 150, color: '#46ffb0', strength: 0.75, kind: 'shiori' })

  return { plan, furnished, city, width, height, screen, ground: p.canvas(), indoor, solid, props, walls, pools, centre }
}

/** Where the lab goes on a screen of this size: the middle of its building at the middle. */
function placing(plan: Plan, viewW: number, viewH: number) {
  const [a0, b0, a1, b1] = plan.box
  const ca = ((a0 + a1) / 2) * T
  const cb = ((b0 + b1) / 2) * T
  const OX = Math.round(viewW / 2 - (ca - cb) * 2)
  const OY = Math.round(viewH / 2 - (ca + cb) * YF + WALL_H / 3)
  const screen = (a: number, b: number, z = 0): [number, number] => [OX + (a - b) * 2, OY + (a + b) * YF - z]
  return { screen, OX, OY }
}

/** The floor point under a pixel. */
function unscreen(screen: (a: number, b: number) => [number, number], x: number, y: number): [number, number] {
  const [ox, oy] = screen(0, 0)
  const dx = x - ox
  const dy = y - oy
  return [(dx / 2 + dy / YF) / 2, (dy / YF - dx / 2) / 2]
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
