import { box, cylinder, iso, onLeftFace, shades, SQUASH, YF, type Sprite } from './iso'
import { mix, noise, Painter, rand, type Point } from './px'

/**
 * The lab's furniture, painted pixel by pixel. Each piece is a sprite standing on its floor
 * origin, its footprint (for who is in front of whom) and, when it moves, an `anim` that paints
 * its live pixels over it every frame: bubbles, blinking lights, holograms.
 */
export interface Prop {
  sprite: Sprite
  /** Footprint along the floor axes, in eighths of a tile. */
  da: number
  db: number
  anim?: (ctx: CanvasRenderingContext2D, x: number, y: number, t: number, flip: number) => void
  /** Where it gives light when the room is dark: offsets from its floor origin, radius, colour. */
  lights?: Light[]
}

export interface Light {
  dx: number
  dy: number
  r: number
  color: string
  /** A beam instead of a pool: its direction on screen, as headlights throw it along the road. */
  beam?: [number, number]
}

/** The light a prop throws on the floor: a pool centred at (a, b) of its own floor units. */
function lamp(a: number, b: number, r: number, color: string): Light {
  const [dx, dy] = iso(a, b)
  return { dx, dy, r, color }
}

export const OUTLINE = '#18202b'
const WHITE = '#dfe6ec'
const DARK = '#2a3140'

type At = (a: number, b: number, z?: number) => Point

/** Paints a prop of footprint da × db reaching `top` pixels up, its origin placed with room around it. */
function paint(da: number, db: number, top: number, draw: (p: Painter, at: At, ax: number, ay: number) => void, extra = 8): { sprite: Sprite; ax: number; ay: number } {
  const w = 2 * (da + db) + 2 * extra
  const h = Math.ceil((da + db) * YF) + top + 2 * extra
  const ax = 2 * db + extra
  const ay = top + extra
  const p = new Painter(w, h)
  const at: At = (a, b, z = 0) => {
    const [x, y] = iso(a, b, z)
    return [ax + x, ay + y]
  }
  draw(p, at, ax, ay)
  p.outline(OUTLINE)
  return { sprite: { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined }, ax, ay }
}

/** A soft shadow on the floor around a footprint, toward the front (light comes from the back left). */
function shadow(p: Painter, at: At, a: number, b: number, da: number, db: number) {
  p.poly([at(a, b), at(a + da + 2, b), at(a + da + 2, b + db + 2), at(a, b + db + 2)], '#0b1220', 0.16)
}

/** A pixel on the live canvas. */
export function dot(ctx: CanvasRenderingContext2D, x: number, y: number, color: string, alpha = 1, size = 1) {
  ctx.globalAlpha = alpha
  ctx.fillStyle = color
  ctx.fillRect(Math.round(x), Math.round(y), size, size)
  ctx.globalAlpha = 1
}

/** A soft light added over the scene. */
export function glow(ctx: CanvasRenderingContext2D, x: number, y: number, r: number, color: string, alpha: number) {
  const g = ctx.createRadialGradient(x, y, 0, x, y, r)
  g.addColorStop(0, color)
  g.addColorStop(1, 'transparent')
  ctx.save()
  ctx.globalCompositeOperation = 'lighter'
  ctx.globalAlpha = alpha
  ctx.fillStyle = g
  ctx.fillRect(x - r, y - r, 2 * r, 2 * r)
  ctx.restore()
}

/** Text-like scribbles on a screen: rows of short segments, never words. */
function codeLines(p: Painter, at: At, a: number, b: number, z: number, wide: number, high: number, palette: string[], seed: number) {
  const r = rand(seed)
  for (let v = high - 2; v >= 1; v -= 2) {
    let u = 1 + Math.floor(r() * 3) * 2
    while (u < wide - 2) {
      const len = 1 + Math.floor(r() * 5)
      const c = palette[Math.floor(r() * palette.length)]
      for (let k = 0; k < len && u + k < wide - 1; k++) onLeftFace(p, at, a, b, z, u + k, v, c)
      u += len + 1 + Math.floor(r() * 2)
    }
  }
}

/** A monitor on a stand, its screen looking +b. Returns where its screen is, for live pixels. */
function monitor(p: Painter, at: At, a: number, b: number, z: number, wide: number, high: number, accent: string, seed: number) {
  box(p, at, a + wide / 2 - 1, b, z, 2, 2, 4, shades('#4a5466'))
  box(p, at, a + wide / 2 - 3, b - 1, z, 6, 4, 1, shades('#4a5466'))
  box(p, at, a, b, z + 4, wide, 1, high, shades(DARK))
  // The screen, inset by a pixel, glowing.
  p.emitting = true
  for (let u = 1; u < wide * 2 - 1; u++) for (let v = 1; v < high - 1; v++) onLeftFace(p, at, a, b + 1, z + 4, u, v, '#0d2433')
  codeLines(p, at, a, b + 1, z + 4, wide * 2, high, [accent, '#7fd6ff', '#5f7d96', '#cfe9ff'], seed)
  p.emitting = false
}

/** A long bench desk with three screens: engineering's. */
export function benchDesk(accent: string, seed = 1): Prop {
  const da = 40
  const db = 12
  let cursor: Point = [0, 0]
  const { sprite, ax, ay } = paint(da, db, 46, (p, at) => {
    shadow(p, at, 0, 0, da, db)
    box(p, at, 1, 1, 0, 2, db - 2, 20, shades('#9aa6b4'))
    box(p, at, da - 3, 1, 0, 2, db - 2, 20, shades('#9aa6b4'))
    box(p, at, 3, 1, 8, da - 6, 1, 12, shades('#aeb8c4'))
    box(p, at, 0, 0, 20, da, db, 3, shades(WHITE))
    // The department's stripe along the front edge.
    for (let u = 0; u < da * 2; u++) onLeftFace(p, at, 0, db, 20, u, 1, accent)
    for (let k = 0; k < 3; k++) {
      monitor(p, at, 3 + k * 13, 2, 23, 10, 13, accent, seed * 7 + k)
      box(p, at, 4 + k * 13, 6, 23, 8, 3, 1, shades('#c4ccd6'))
      for (let u = 1; u < 15; u += 2) onLeftFace(p, at, 4 + k * 13, 9, 23, u, 1, '#8994a3')
    }
    cylinder(p, ...at(35, 9, 23), 2, 4, '#f2f2f0')
    p.poly([at(2, 7, 23), at(7, 7, 23), at(7, 11, 23), at(2, 11, 23)], '#f6f4ec')
    p.poly([at(3, 8, 23), at(8, 8, 23), at(8, 11, 23), at(3, 11, 23)], '#ffffff')
    const [cx, cy] = at(3, 3, 27)
    cursor = [cx + 3, cy + 3]
  })
  const [cx, cy] = [cursor[0] - ax, cursor[1] - ay]
  return {
    sprite,
    da,
    db,
    lights: [0, 1, 2].map((k) => lamp(8 + k * 13, 15, 26, '#5fc8ff')),
    anim(ctx, x, y, t, flip) {
      // A blinking cursor.
      if (Math.floor(t * 2) % 2 === 0) dot(ctx, x + flip * cx, y + cy, '#cfe9ff', 1, 2)
    },
  }
}

/** An L-shaped desk with a wide screen, books and a plant: data's. */
export function cornerDesk(accent: string, seed = 2): Prop {
  const da = 30
  const db = 26
  const { sprite } = paint(da, db, 50, (p, at) => {
    shadow(p, at, 0, 0, da, 12)
    shadow(p, at, 0, 12, 12, 14)
    box(p, at, 1, 1, 0, 2, 10, 20, shades('#9aa6b4'))
    box(p, at, da - 3, 1, 0, 2, 10, 20, shades('#9aa6b4'))
    box(p, at, 1, db - 3, 0, 10, 2, 20, shades('#9aa6b4'))
    box(p, at, 0, 0, 20, da, 12, 3, shades(WHITE))
    box(p, at, 0, 12, 20, 12, db - 12, 3, shades(WHITE))
    for (let u = 0; u < 24; u++) onLeftFace(p, at, 0, db, 20, u, 1, accent)
    for (let u = 24; u < da * 2; u++) onLeftFace(p, at, 0, 12, 20, u, 1, accent)
    monitor(p, at, 12, 2, 23, 16, 15, accent, seed)
    // Books, a stack of paper, a lamp, a plant.
    const r = rand(seed)
    for (let k = 0; k < 4; k++) box(p, at, 2 + k * 2, 2, 23, 2, 6, 7 + Math.floor(r() * 3), shades(['#c0504d', '#3f6fb5', '#e0b14a', '#4c9a7a'][k]), false)
    box(p, at, 3, 14, 23, 6, 7, 3, shades('#f4f2ea'), false)
    box(p, at, 4, 15, 26, 5, 6, 1, shades('#fbfaf6'), false)
    cylinder(p, ...at(6, 22, 23), 3, 5, '#d8dde3')
    p.line(...at(6, 22, 28), ...at(9, 19, 40), '#5d6878')
    box(p, at, 7, 17, 38, 4, 4, 3, shades(accent))
    cylinder(p, ...at(26, 8, 23), 3, 5, '#c9cfd6')
    for (let k = 0; k < 14; k++) {
      const [x, y] = at(26, 8, 28)
      p.ellipse(x - 4 + r() * 8, y - 3 - r() * 9, 2, 1.5, ['#3d8f5a', '#57b06f', '#2f7048'][k % 3])
    }
  })
  return {
    sprite,
    da,
    db,
    lights: [lamp(20, 16, 30, '#7fc8ff'), lamp(9, 20, 30, '#ffd9a0')],
  }
}

/** A round table with a hologram turning above it. */
export function holoTable(accent: string): Prop {
  const da = 18
  const db = 18
  const { sprite } = paint(da, db, 60, (p, at) => {
    const [cx, cy] = at(9, 9)
    p.ellipse(cx + 2, cy + 1, 26, 26 * SQUASH, '#0b1220', 0.16)
    cylinder(p, cx, cy, 6, 14, '#5b6677')
    cylinder(p, cx, cy - 14, 24, 3, '#2b3443', '#1b2533')
    p.emitting = true
    p.ellipse(cx, cy - 17, 20, 20 * SQUASH, '#13202c')
    for (let k = 0; k < 40; k++) {
      const th = (k / 40) * Math.PI * 2
      p.set(cx + Math.cos(th) * 22, cy - 17 + Math.sin(th) * 22 * SQUASH, k % 4 === 0 ? '#9ff3ff' : accent)
    }
    p.emitting = false
    cylinder(p, cx, cy - 17, 4, 2, '#7a8696', '#bfe9ff')
  })
  const [hx, hy] = iso(9, 9)
  return {
    sprite,
    da,
    db,
    lights: [lamp(9, 9, 46, accent)],
    anim(ctx, x, y, t) {
      const cx = x + hx
      const cy = y + hy - 40
      glow(ctx, cx, cy + 10, 30, accent, 0.25)
      // A light cone, then a turning globe of points.
      for (let k = 0; k < 18; k++) dot(ctx, cx - 3 + (k % 7), cy + 4 + k, '#9ff3ff', 0.08 + 0.04 * Math.sin(t * 6 + k))
      for (let lat = -3; lat <= 3; lat++) {
        const phi = (lat / 4) * (Math.PI / 2)
        for (let lon = 0; lon < 16; lon++) {
          const th = (lon / 16) * Math.PI * 2 + t * 1.2
          const X = Math.cos(phi) * Math.cos(th) * 11
          const Z = Math.cos(phi) * Math.sin(th) * 11
          const Y = Math.sin(phi) * 11
          dot(ctx, cx + X, cy + Y + Z * 0.25, Z > 0 ? '#c9fbff' : accent, Z > 0 ? 0.95 : 0.45)
        }
      }
    },
  }
}

/** A lab bench with a microscope, test tubes and a beaker bubbling: research's. */
export function microscopeBench(accent: string): Prop {
  const da = 30
  const db = 12
  const { sprite } = paint(da, db, 50, (p, at) => {
    shadow(p, at, 0, 0, da, db)
    box(p, at, 0, 0, 0, da, db, 20, shades('#c9d1da'))
    for (let u = 2; u < da * 2 - 2; u += 20) for (let v = 3; v < 18; v++) onLeftFace(p, at, 0, db, 0, u, v, '#aab4c0')
    for (let u = 0; u < da * 2; u++) onLeftFace(p, at, 0, db, 0, u, 17, accent)
    box(p, at, 0, 0, 20, da, db, 2, shades('#3a4352'))
    // The microscope.
    box(p, at, 4, 3, 22, 6, 6, 2, shades('#e9edf1'))
    box(p, at, 5, 3, 24, 2, 2, 12, shades('#e9edf1'))
    box(p, at, 6, 4, 33, 4, 2, 3, shades('#e9edf1'))
    box(p, at, 8, 4, 28, 2, 2, 6, shades('#3a4352'))
    box(p, at, 5, 6, 27, 4, 3, 1, shades('#5d6878'))
    // A rack of coloured tubes.
    box(p, at, 14, 3, 22, 10, 3, 3, shades('#d6dce3'))
    ;['#e0607e', '#5fd18e', '#62b5ff', '#f0c04a', '#b27cff'].forEach((c, k) => {
      const [x, y] = at(15 + k * 2, 4, 25)
      p.rect(x, y - 7, 1, 7, '#e8f6ff')
      p.rect(x, y - 4, 1, 4, c)
    })
    // The beaker.
    const [bx, by] = at(26, 8, 22)
    cylinder(p, bx, by, 4, 9, '#cfeaff', '#e8f6ff', 0.55)
    p.emitting = true
    cylinder(p, bx, by, 4, 5, accent, mix(accent, '#ffffff', 0.4), 0.8)
    p.emitting = false
  })
  const [bx, by] = iso(26, 8, 22)
  return {
    sprite,
    da,
    db,
    lights: [lamp(26, 14, 18, accent)],
    anim(ctx, x, y, t, flip) {
      const cx = x + flip * bx
      for (let k = 0; k < 4; k++) {
        const ph = (t * 0.9 + k * 0.27) % 1
        dot(ctx, cx - 2 + ((k * 3) % 5) * flip, y + by - 1 - ph * 6, '#ffffff', 1 - ph)
      }
      glow(ctx, cx, y + by - 4, 10, accent, 0.3)
    },
  }
}

/** A glass capsule full of liquid, something asleep inside, bubbles rising. */
export function capsule(liquid: string, seed = 3): Prop {
  const R = 15
  const TUBE = 66
  const BASE = 10
  const da = 12
  const db = 12
  const { sprite } = paint(da, db, BASE + TUBE + 34, (p, at) => {
    const [cx, cy] = at(6, 6)
    p.ellipse(cx + 3, cy + 2, R + 6, (R + 6) * SQUASH, '#0b1220', 0.18)
    cylinder(p, cx, cy, R + 2, BASE, '#465062', '#5d687a')
    p.emitting = true
    for (let k = 0; k < 9; k++) {
      const th = Math.PI * (0.1 + (k / 8) * 0.8)
      p.set(cx + Math.cos(th) * (R + 1), cy - 4 + Math.sin(th) * (R + 1) * SQUASH, k % 2 ? '#7ff0ff' : '#3cc7e0')
    }
    // The liquid, darker at the edges.
    cylinder(p, cx, cy - BASE, R - 1, TUBE - 12, liquid, mix(liquid, '#ffffff', 0.35))
    const r = rand(seed)
    // Something curled up, sleeping, in the middle.
    const sx = cx
    const sy = cy - BASE - 28
    const body = mix(liquid, '#081018', 0.62)
    p.ellipse(sx, sy, 6, 9, body)
    p.ellipse(sx - 1, sy - 10, 4.5, 4, body)
    p.ellipse(sx + 3, sy + 6, 4, 3, body)
    for (let k = 0; k < 10; k++) p.set(sx - 6 + k, sy + 10 + Math.round(Math.sin(k * 0.8) * 1.5), body)
    p.set(sx - 2, sy - 11, mix(liquid, '#ffffff', 0.5))
    for (let k = 0; k < 5; k++) p.line(sx, sy - 14, sx - 4 + r() * 8, cy - BASE - TUBE + 2, mix(liquid, '#081018', 0.4), 0.5)
    // The glass: a cylinder of faint light with bright edges and a long highlight on the left.
    for (let y = cy - BASE - TUBE; y < cy - BASE; y++) {
      for (let x = cx - R; x < cx + R; x++) {
        const k = (x + 0.5 - cx) / R
        const edge = Math.abs(k) > 0.86
        p.set(x, y, edge ? '#bff3ff' : '#d9fbff', edge ? 0.5 : 0.08)
      }
      p.set(cx - R + 3, y, '#ffffff', 0.55)
      if (y === cy - BASE - 1) p.emitting = false
      p.set(cx - R + 4, y, '#ffffff', 0.3)
      p.set(cx + R - 4, y, '#ffffff', 0.18)
    }
    // Metal rings and the cap, with pipes into the ceiling.
    cylinder(p, cx, cy - BASE - TUBE + 2, R + 2, 3, '#5b6677')
    cylinder(p, cx, cy - BASE - TUBE - 1, R + 1, 10, '#465062', '#68748a')
    cylinder(p, cx, cy - BASE - TUBE - 11, 6, 6, '#3a4352')
    p.rect(cx - 9, cy - BASE - TUBE - 32, 3, 22, '#3a4352')
    p.rect(cx - 9, cy - BASE - TUBE - 32, 1, 22, '#5d687a')
    p.rect(cx + 5, cy - BASE - TUBE - 32, 4, 24, '#2e3644')
    p.rect(cx + 5, cy - BASE - TUBE - 32, 1, 24, '#4d586a')
    p.emitting = true
    p.rect(cx - 2, cy - BASE - TUBE - 6, 4, 2, '#ff5468')
    p.emitting = false
  })
  const [cx, cy] = iso(6, 6)
  const r = rand(seed * 13)
  const capsuleLight = lamp(6, 8, 44, liquid)
  const bubbles = Array.from({ length: 16 }, () => ({
    x: (r() * 2 - 1) * (R - 4),
    speed: 0.15 + r() * 0.3,
    ph: r(),
    big: r() > 0.75,
  }))
  return {
    sprite,
    da,
    db,
    lights: [capsuleLight],
    anim(ctx, x, y, t) {
      const bx = x + cx
      const by = y + cy - BASE
      const surface = by - (TUBE - 12)
      glow(ctx, bx, by - 30, 34, liquid, 0.28 + 0.06 * Math.sin(t * 2 + seed))
      for (const b of bubbles) {
        const ph = (t * b.speed + b.ph) % 1
        const yy = by - 4 - ph * (TUBE - 18)
        const xx = bx + b.x + Math.sin(t * 3 + b.ph * 9) * 1.2
        dot(ctx, xx, yy, '#eafffb', 0.85 * (1 - ph * 0.5), b.big ? 2 : 1)
      }
      // The surface ripples.
      for (let k = -R + 2; k < R - 2; k++) dot(ctx, bx + k, surface + Math.round((R - 1) * SQUASH * Math.sqrt(1 - (k / (R - 1)) ** 2) + Math.sin(t * 4 + k * 0.6)), mix(liquid, '#ffffff', 0.6), 0.6)
      // The cap's light blinks.
      if (Math.floor(t * 1.5 + seed) % 3 !== 0) dot(ctx, bx - 2, by - TUBE - 6, '#ff8a98', 1, 2)
    },
  }
}

/** A server rack: rows of drives and lights. */
export function rack(seed = 4): Prop {
  const da = 10
  const db = 10
  const H = 50
  const leds: [number, number][] = []
  const { sprite, ax, ay } = paint(da, db, H + 4, (p, at) => {
    shadow(p, at, 0, 0, da, db)
    box(p, at, 0, 0, 0, da, db, H, shades('#2c3442'))
    for (let v = 4; v < H - 3; v += 4) {
      for (let u = 2; u < da * 2 - 2; u++) onLeftFace(p, at, 0, db, 0, u, v, '#1c2230')
      for (let u = 2; u < da * 2 - 2; u++) onLeftFace(p, at, 0, db, 0, u, v + 1, '#3b4556')
      const [x, y] = at(0, db, 0)
      leds.push([x + 3, y + 1 - v - 2], [x + 5, y + 2 - v - 2], [x + 15, y + 7 - v - 2])
    }
    for (let v = 3; v < H - 3; v += 2)
      for (let u = 3; u < da * 2 - 3; u += 2) {
        const [x, y] = at(da, 0, 0)
        p.set(x - u, y + Math.floor((u * YF) / 2) - v, '#1a202b')
      }
  })
  const r = rand(seed)
  const live = leds.map(([x, y]) => ({
    x: x - ax,
    y: y - ay,
    ph: r() * 10,
    color: ['#5dff9a', '#5dff9a', '#ffcf5a', '#67e3ff'][Math.floor(r() * 4)],
  }))
  return {
    sprite,
    da,
    db,
    lights: [lamp(5, 14, 18, '#5dff9a')],
    anim(ctx, x, y, t, flip) {
      for (const l of live) if (Math.sin(t * (2 + l.ph) + l.ph) > -0.2) dot(ctx, x + flip * l.x, y + l.y, l.color)
    },
  }
}

export type BoardKind = 'math' | 'diagram' | 'graph'

/** A whiteboard on wheels, written on: equations, a diagram or a chart with sticky notes. Never readable. */
export function whiteboard(kind: BoardKind, seed = 5): Prop {
  const da = 28
  const db = 2
  const { sprite } = paint(da, db, 50, (p, at) => {
    shadow(p, at, 0, -2, da, 6)
    for (const a of [1, da - 3]) {
      box(p, at, a, -2, 0, 2, 6, 1, shades('#5d6878'))
      box(p, at, a, 0, 1, 2, 2, 14, shades('#9aa6b4'))
    }
    box(p, at, 0, 0, 14, da, db, 32, shades('#c9d1da'))
    const W = da * 2
    for (let u = 2; u < W - 2; u++) for (let v = 2; v < 30; v++) onLeftFace(p, at, 0, db, 14, u, v, '#f5f7f9')
    box(p, at, 4, 2, 15, da - 8, 2, 1, shades('#9aa6b4'))
    const r = rand(seed)
    const put = (u: number, v: number, c: string) => {
      if (u > 2 && u < W - 3 && v > 2 && v < 29) onLeftFace(p, at, 0, db, 14, u, v, c)
    }
    const line = (u0: number, v0: number, u1: number, v1: number, c: string) => {
      const n = Math.max(Math.abs(u1 - u0), Math.abs(v1 - v0), 1)
      for (let k = 0; k <= n; k++) put(Math.round(u0 + ((u1 - u0) * k) / n), Math.round(v0 + ((v1 - v0) * k) / n), c)
    }
    const ink = ['#2c4a9a', '#1f2430', '#c23b4a', '#2d8a5a']
    if (kind === 'math') {
      for (let v = 26; v > 5; v -= 5) {
        let u = 4 + Math.floor(r() * 4)
        const c = ink[Math.floor(r() * 3)]
        while (u < W - 8) {
          const len = 2 + Math.floor(r() * 6)
          for (let k = 0; k < len; k++) put(u + k, v + Math.round(Math.sin((u + k) * 1.7 + v) * 0.6 + (r() > 0.8 ? 1 : 0)), c)
          u += len + 2
          if (r() > 0.75) {
            line(u, v - 1, u + 2, v + 1, c)
            line(u, v + 1, u + 2, v - 1, c)
            u += 4
          }
        }
      }
      line(6, 4, 30, 4, ink[2])
    } else if (kind === 'diagram') {
      const boxes: [number, number][] = [
        [6, 20],
        [24, 22],
        [40, 16],
        [14, 7],
        [34, 6],
      ]
      for (const [u, v] of boxes) {
        line(u, v, u + 9, v, ink[0])
        line(u, v + 5, u + 9, v + 5, ink[0])
        line(u, v, u, v + 5, ink[0])
        line(u + 9, v, u + 9, v + 5, ink[0])
        for (let k = 2; k < 8; k += 2) put(u + k, v + 3, ink[1])
      }
      line(15, 22, 24, 24, ink[1])
      line(33, 24, 40, 19, ink[1])
      line(10, 19, 16, 12, ink[3])
      line(24, 9, 34, 8, ink[3])
      line(44, 15, 40, 11, ink[2])
    } else {
      line(6, 4, 6, 26, ink[1])
      line(6, 4, 40, 4, ink[1])
      let v = 8
      for (let u = 7; u < 40; u++) {
        v = Math.max(5, Math.min(25, v + (r() > 0.45 ? 1 : -1) * (r() > 0.5 ? 1 : 0)))
        put(u, v, ink[2])
      }
      for (let u = 8; u < 38; u += 4) for (let k = 5; k < 5 + Math.floor(3 + r() * 10); k++) put(u, k, ink[0])
      const notes = ['#ffe270', '#ff9fc4', '#9fe6ff', '#b8f28a']
      for (let k = 0; k < 4; k++) {
        const u0 = 42 + (k % 2) * 6
        const v0 = 22 - Math.floor(k / 2) * 7
        for (let u = u0; u < u0 + 5; u++) for (let vv = v0; vv < v0 + 5; vv++) put(u, vv, notes[k])
        put(u0 + 1, v0 + 3, '#6b6b6b')
        put(u0 + 2, v0 + 3, '#6b6b6b')
      }
    }
  })
  return { sprite, da, db }
}

/** A coffee counter with a machine, cups and a water cooler. */
export function coffeeCounter(accent: string): Prop {
  const da = 26
  const db = 9
  const { sprite } = paint(da, db, 56, (p, at) => {
    shadow(p, at, 0, 0, da, db)
    box(p, at, 0, 0, 0, da, db, 20, shades('#e3e8ed'))
    for (let u = 0; u < da * 2; u += 18) for (let v = 2; v < 18; v++) onLeftFace(p, at, 0, db, 0, u, v, '#c7cfd8')
    box(p, at, 0, 0, 20, da, db, 2, shades('#3a4352'))
    box(p, at, 3, 1, 22, 8, 7, 14, shades('#2e3644'))
    box(p, at, 4, 7, 26, 6, 1, 3, shades('#1a202b'))
    p.emitting = true
    for (let u = 2; u < 9; u++) onLeftFace(p, at, 3, 8, 22, u, 11, accent)
    onLeftFace(p, at, 3, 8, 22, 12, 11, '#ff5468')
    p.emitting = false
    for (let k = 0; k < 3; k++) cylinder(p, ...at(15 + k * 3, 5, 22), 1.5, 3, '#f2f2f0')
    // The water cooler at the end.
    cylinder(p, ...at(23, 4, 22), 3.5, 8, '#d0d8e0')
    cylinder(p, ...at(23, 4, 30), 3.5, 9, '#8fd8ff', '#c4ecff', 0.7)
  })
  const [mx, my] = iso(7, 8, 30)
  return {
    sprite,
    da,
    db,
    lights: [lamp(7, 14, 20, accent)],
    anim(ctx, x, y, t, flip) {
      // Steam from the machine.
      for (let k = 0; k < 5; k++) {
        const ph = (t * 0.5 + k / 5) % 1
        dot(ctx, x + flip * mx + Math.sin(ph * 8 + k) * 2, y + my - 6 - ph * 14, '#ffffff', 0.5 * (1 - ph))
      }
    },
  }
}

/** A low sofa in the lounge. */
export function sofa(color: string): Prop {
  const da = 26
  const db = 10
  const { sprite } = paint(da, db, 24, (p, at) => {
    shadow(p, at, 0, 0, da, db)
    // The seat, the backrest between the two arms, two cushions.
    box(p, at, 0, 0, 0, da, db, 8, shades(color))
    box(p, at, 3, 0, 8, da - 6, 3, 11, shades(color))
    box(p, at, 3, 3, 8, (da - 6) / 2, db - 3, 2, shades(mix(color, '#ffffff', 0.12)))
    box(p, at, 3 + (da - 6) / 2, 3, 8, (da - 6) / 2, db - 3, 2, shades(mix(color, '#ffffff', 0.12)))
    box(p, at, 0, 0, 8, 3, db, 7, shades(color))
    box(p, at, da - 3, 0, 8, 3, db, 7, shades(color))
  })
  return { sprite, da, db }
}

/** A tall plant in a white pot. */
export function plant(seed = 6): Prop {
  const { sprite } = paint(6, 6, 44, (p, at) => {
    const [cx, cy] = at(3, 3)
    p.ellipse(cx + 2, cy + 1, 9, 9 * SQUASH, '#0b1220', 0.16)
    cylinder(p, cx, cy, 6, 12, '#e8edf2')
    const r = rand(seed)
    for (let k = 0; k < 26; k++) {
      const th = -Math.PI / 2 + (r() - 0.5) * 2.4
      const len = 8 + r() * 22
      const x = cx + Math.cos(th) * len * 0.5
      const y = cy - 12 + Math.sin(th) * len
      p.line(cx, cy - 12, x, y, '#2f6a45')
      p.ellipse(x, y, 2.5, 1.5, ['#3d8f5a', '#57b06f', '#2f7048', '#76c785'][k % 4])
    }
  })
  return { sprite, da: 6, db: 6 }
}

/** An office chair, its back on the +b side. */
export function chair(color: string): Prop {
  const { sprite } = paint(6, 6, 24, (p, at) => {
    const [cx, cy] = at(3, 3)
    p.ellipse(cx + 1, cy + 1, 7, 7 * SQUASH, '#0b1220', 0.16)
    for (const [a, b] of [
      [0, 3],
      [6, 3],
      [3, 0],
      [3, 6],
    ])
      p.line(cx, cy, ...at(a, b), '#3a4352')
    p.rect(cx, cy - 10, 1, 10, '#5d6878')
    box(p, at, 0, 0, 10, 6, 6, 2, shades(color))
    box(p, at, 0, 5, 12, 6, 1, 10, shades(color))
  })
  return { sprite, da: 6, db: 6 }
}

/** A glass wall between two areas: a frame and a faint pane. */
export function glassWall(length: number): Prop {
  const da = length
  const db = 1
  const { sprite } = paint(da, db, 30, (p, at) => {
    box(p, at, 0, 0, 0, da, db, 2, shades('#8d99a8'))
    for (let u = 0; u < da * 2; u++) for (let v = 2; v < 26; v++) onLeftFace(p, at, 0, db, 0, u, v, (u + v) % 23 === 0 ? '#ffffff' : '#bfe6f5', (u + v) % 23 === 0 ? 0.5 : 0.18)
    for (let u = 0; u < da * 2; u += 24) for (let v = 2; v < 26; v++) onLeftFace(p, at, 0, db, 0, u, v, '#9fb0c0')
    box(p, at, 0, 0, 26, da, db, 2, shades('#9fb0c0'))
  })
  return { sprite, da, db }
}

/** A tall standing screen with a live chart. */
export function totem(accent: string): Prop {
  const da = 4
  const db = 12
  const { sprite } = paint(da, db, 56, (p, at) => {
    shadow(p, at, 0, 0, da, db)
    box(p, at, 0, 0, 0, da, db, 4, shades('#5b6677'))
    box(p, at, 1, 5, 4, 2, 2, 14, shades('#8d99a8'))
    box(p, at, 0, 0, 18, da, db, 34, shades(DARK))
  })
  return {
    sprite,
    da,
    db,
    lights: [lamp(9, 6, 30, accent)],
    anim(ctx, x, y, t, flip) {
      const [x0, y0] = iso(da, 0, 18)
      for (let u = 2; u < db * 2 - 1; u++) {
        const h = 6 + Math.round((Math.sin(t * 2 + u * 0.7) * 0.5 + 0.5) * 10 + noise(u * 0.4, t, 3) * 6)
        for (let v = 2; v < 31; v++) {
          const X = x + flip * (x0 - u)
          const Y = y + y0 + Math.floor((u * YF) / 2) - v
          dot(ctx, X, Y, v < h ? (v > h - 2 ? '#e8fbff' : accent) : '#0d2433', v < h ? 0.95 : 1)
        }
      }
    },
  }
}

/** The same prop seen from the other side: drawn along b instead of a. */
export function mirrored(prop: Prop): Prop {
  const c = prop.sprite.canvas
  const m = document.createElement('canvas')
  m.width = c.width
  m.height = c.height
  const ctx = m.getContext('2d')!
  ctx.translate(c.width, 0)
  ctx.scale(-1, 1)
  ctx.drawImage(c, 0, 0)
  const flip = (src?: HTMLCanvasElement) => {
    if (!src) return undefined
    const out = document.createElement('canvas')
    out.width = src.width
    out.height = src.height
    const g = out.getContext('2d')!
    g.translate(src.width, 0)
    g.scale(-1, 1)
    g.drawImage(src, 0, 0)
    return out
  }
  return {
    ...prop,
    da: prop.db,
    db: prop.da,
    sprite: { canvas: m, ax: c.width - 1 - prop.sprite.ax, ay: prop.sprite.ay, glow: flip(prop.sprite.glow) },
  }
}
