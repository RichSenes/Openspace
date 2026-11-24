import { mix, Painter, type Point } from './px'

/**
 * The isometric grid, Habbo's way: a floor tile is a 32×16 diamond, lines run two pixels across
 * for one down. Positions are in units of an eighth of a tile along the two floor axes, so every
 * corner lands on a whole pixel: +a goes down-right on screen, +b down-left, z is pixels up.
 */
export const TILE = 8

export function iso(a: number, b: number, z = 0): Point {
  return [(a - b) * 2, a + b - z]
}

/** A sprite painted once: its canvas and the pixel that sits on its floor origin. */
export interface Sprite {
  canvas: HTMLCanvasElement
  ax: number
  ay: number
}

/**
 * Paints a sprite whose floor origin (a = b = z = 0) is at (ax, ay): `draw` gets the painter
 * and a projection from the prop's own a, b, z to its pixels.
 */
export function sprite(w: number, h: number, ax: number, ay: number, draw: (p: Painter, at: (a: number, b: number, z?: number) => Point) => void, outline: string | null = '#18202b'): Sprite {
  const p = new Painter(w, h)
  draw(p, (a, b, z = 0) => {
    const [x, y] = iso(a, b, z)
    return [ax + x, ay + y]
  })
  if (outline) p.outline(outline)
  return { canvas: p.canvas(), ax, ay }
}

export interface Shades {
  top: string
  left: string
  right: string
}

/** Three shades of a colour for a box lit from the top left. */
export function shades(color: string, depth = 1): Shades {
  return { top: mix(color, '#ffffff', 0.12 * depth), left: color, right: mix(color, '#0b1020', 0.28 * depth) }
}

export interface BoxFaces {
  top: Set<number>
  left: Set<number>
  right: Set<number>
}

/**
 * A box from (a, b, z) of size (da, db, h): its top, its front-left face (looking +b) and its
 * front-right face (looking +a). The top's front rims and the front corner catch the light.
 */
export function box(p: Painter, at: (a: number, b: number, z?: number) => Point, a: number, b: number, z: number, da: number, db: number, h: number, c: Shades, rim = true): BoxFaces {
  const P = (x: number, y: number, zz: number) => at(x, y, zz)
  const left = p.poly([P(a, b + db, z), P(a + da, b + db, z), P(a + da, b + db, z + h), P(a, b + db, z + h)], c.left)
  const right = p.poly([P(a + da, b, z), P(a + da, b + db, z), P(a + da, b + db, z + h), P(a + da, b, z + h)], c.right)
  const top = p.poly([P(a, b, z + h), P(a + da, b, z + h), P(a + da, b + db, z + h), P(a, b + db, z + h)], c.top)
  if (rim) {
    const light = mix(c.top, '#ffffff', 0.45)
    for (const i of top) {
      const x = i % p.w
      const y = Math.floor(i / p.w)
      if (!top.has(i + p.w)) p.set(x, y, light)
    }
    const corner = mix(c.left, '#ffffff', 0.25)
    for (const i of left) {
      if (right.has(i + 1)) p.set(i % p.w, Math.floor(i / p.w), corner)
    }
  }
  return { top, left, right }
}

/**
 * Paints on the front-left face of a box (the one looking +b), in that face's own pixels:
 * `u` runs along it from its left end, `v` up from its bottom.
 */
export function onLeftFace(p: Painter, at: (a: number, b: number, z?: number) => Point, a: number, b: number, z: number, u: number, v: number, color: string, alpha = 1) {
  const [x, y] = at(a, b, z)
  p.set(x + u, y + Math.floor(u / 2) - v, color, alpha)
}

/** The same on the front-right face (looking +a): `u` runs leftwards from its back (right) end. */
export function onRightFace(p: Painter, at: (a: number, b: number, z?: number) => Point, a: number, b: number, z: number, u: number, v: number, color: string, alpha = 1) {
  const [x, y] = at(a, b, z)
  p.set(x - u, y + Math.floor(u / 2) - v, color, alpha)
}

/**
 * An upright cylinder standing on (cx, cy), seen from above at Habbo's angle: an ellipse twice
 * as wide as tall, banded from light on the left to dark on the right.
 */
export function cylinder(p: Painter, cx: number, cy: number, rx: number, h: number, color: string, top?: string, alpha = 1) {
  const ry = rx / 2
  const band = (x: number) => {
    const k = (x + 0.5 - (cx - rx)) / (2 * rx)
    const step = Math.min(4, Math.max(0, Math.floor(k * 5)))
    return [mix(color, '#ffffff', 0.18), mix(color, '#ffffff', 0.06), color, mix(color, '#0b1020', 0.18), mix(color, '#0b1020', 0.34)][step]
  }
  p.ellipse(cx, cy, rx, ry, (x) => band(x), alpha)
  for (let x = Math.floor(cx - rx); x < Math.ceil(cx + rx); x++) {
    const dx = (x + 0.5 - cx) / rx
    if (Math.abs(dx) > 1) continue
    const edge = ry * Math.sqrt(1 - dx * dx)
    for (let y = Math.round(cy - h); y <= Math.round(cy + edge - 0.5); y++) p.set(x, y, band(x), alpha)
  }
  p.ellipse(cx, cy - h, rx, ry, top ?? mix(color, '#ffffff', 0.22), alpha)
}
