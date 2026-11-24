/**
 * A tiny pixel painter: hard pixels in a plain RGBA buffer, no anti-aliasing. Shapes are filled
 * where a pixel's centre falls inside them, so iso faces that share an edge never overlap or
 * leave gaps. Sprites are painted once here and turned into canvases.
 */

type Rgb = [number, number, number]
const parsed = new Map<string, Rgb>()

export function rgb(color: string): Rgb {
  let c = parsed.get(color)
  if (!c) {
    const h = color.replace('#', '')
    c = [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)]
    parsed.set(color, c)
  }
  return c
}

/** Mixes two colours: 0 is `a`, 1 is `b`. */
export function mix(a: string, b: string, k: number): string {
  const x = rgb(a)
  const y = rgb(b)
  const hex = (v: number) => Math.round(v).toString(16).padStart(2, '0')
  return `#${hex(x[0] + (y[0] - x[0]) * k)}${hex(x[1] + (y[1] - x[1]) * k)}${hex(x[2] + (y[2] - x[2]) * k)}`
}

/** A repeatable random number in [0, 1) for a seed: the same scene every time. */
export function rand(seed: number): () => number {
  let s = seed >>> 0 || 1
  return () => {
    s ^= s << 13
    s ^= s >>> 17
    s ^= s << 5
    return (s >>> 0) / 4294967296
  }
}

/** Smooth value noise in about [0, 1]. */
export function noise(x: number, y: number, seed = 7): number {
  const h = (i: number, j: number) => {
    let n = (i * 374761393 + j * 668265263 + seed * 982451653) | 0
    n = Math.imul(n ^ (n >>> 13), 1274126177)
    return ((n ^ (n >>> 16)) >>> 0) / 4294967296
  }
  const xi = Math.floor(x)
  const yi = Math.floor(y)
  const fx = x - xi
  const fy = y - yi
  const sx = fx * fx * (3 - 2 * fx)
  const sy = fy * fy * (3 - 2 * fy)
  const top = h(xi, yi) + (h(xi + 1, yi) - h(xi, yi)) * sx
  const bottom = h(xi, yi + 1) + (h(xi + 1, yi + 1) - h(xi, yi + 1)) * sx
  return top + (bottom - top) * sy
}

export type Point = [number, number]

export class Painter {
  readonly data: Uint8ClampedArray
  /** While true, what is painted also goes to the light layer: screens, liquids, lamps. */
  emitting = false
  /** The pixels that give light, if any were painted. */
  glow: Uint8ClampedArray | null = null

  readonly w: number
  readonly h: number

  constructor(w: number, h: number) {
    this.w = w
    this.h = h
    this.data = new Uint8ClampedArray(w * h * 4)
  }

  alpha(x: number, y: number): number {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return 0
    return this.data[(y * this.w + x) * 4 + 3]
  }

  /** Paints one pixel, blended over what is there when `a` is below 1. */
  set(x: number, y: number, color: string, a = 1) {
    x = Math.round(x)
    y = Math.round(y)
    if (x < 0 || y < 0 || x >= this.w || y >= this.h || a <= 0) return
    const i = (y * this.w + x) * 4
    const [r, g, b] = rgb(color)
    const d = this.data
    if (a >= 1 || d[i + 3] === 0) {
      d[i] = r
      d[i + 1] = g
      d[i + 2] = b
      d[i + 3] = Math.max(d[i + 3], Math.round(Math.min(1, a) * 255))
      if (a >= 1) d[i + 3] = 255
    } else {
      d[i] = d[i] + (r - d[i]) * a
      d[i + 1] = d[i + 1] + (g - d[i + 1]) * a
      d[i + 2] = d[i + 2] + (b - d[i + 2]) * a
      d[i + 3] = Math.max(d[i + 3], Math.round(a * 255))
    }
    if (this.emitting) {
      this.glow ??= new Uint8ClampedArray(this.w * this.h * 4)
      for (let k = 0; k < 4; k++) this.glow[i + k] = d[i + k]
    }
  }

  /** The light layer as a canvas, or null when nothing here gives light. */
  glowCanvas(): HTMLCanvasElement | null {
    if (!this.glow) return null
    return toCanvas(this.glow, this.w, this.h)
  }

  rect(x: number, y: number, w: number, h: number, color: string, a = 1) {
    for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) this.set(x + i, y + j, color, a)
  }

  /** A hard line, one pixel thick. */
  line(x0: number, y0: number, x1: number, y1: number, color: string, a = 1) {
    x0 = Math.round(x0)
    y0 = Math.round(y0)
    x1 = Math.round(x1)
    y1 = Math.round(y1)
    const dx = Math.abs(x1 - x0)
    const dy = -Math.abs(y1 - y0)
    const sx = x0 < x1 ? 1 : -1
    const sy = y0 < y1 ? 1 : -1
    let err = dx + dy
    for (;;) {
      this.set(x0, y0, color, a)
      if (x0 === x1 && y0 === y1) break
      const e2 = 2 * err
      if (e2 >= dy) {
        err += dy
        x0 += sx
      }
      if (e2 <= dx) {
        err += dx
        y0 += sy
      }
    }
  }

  /** Fills a polygon and returns the pixels it covered, as `y * w + x`. */
  poly(points: Point[], color: string | ((x: number, y: number) => string | null), a = 1): Set<number> {
    const covered = new Set<number>()
    const xs = points.map((p) => p[0])
    const ys = points.map((p) => p[1])
    const x0 = Math.max(0, Math.floor(Math.min(...xs)))
    const x1 = Math.min(this.w - 1, Math.ceil(Math.max(...xs)))
    const y0 = Math.max(0, Math.floor(Math.min(...ys)))
    const y1 = Math.min(this.h - 1, Math.ceil(Math.max(...ys)))
    for (let y = y0; y <= y1; y++) {
      for (let x = x0; x <= x1; x++) {
        if (!inside(points, x + 0.5, y + 0.5)) continue
        const c = typeof color === 'string' ? color : color(x, y)
        if (c) this.set(x, y, c, a)
        covered.add(y * this.w + x)
      }
    }
    return covered
  }

  /** A filled ellipse; `color` may vary per pixel (for shading). */
  ellipse(cx: number, cy: number, rx: number, ry: number, color: string | ((x: number, y: number) => string | null), a = 1) {
    for (let y = Math.floor(cy - ry); y <= Math.ceil(cy + ry); y++) {
      for (let x = Math.floor(cx - rx); x <= Math.ceil(cx + rx); x++) {
        const dx = (x + 0.5 - cx) / rx
        const dy = (y + 0.5 - cy) / ry
        if (dx * dx + dy * dy > 1) continue
        const c = typeof color === 'string' ? color : color(x, y)
        if (c) this.set(x, y, c, a)
      }
    }
  }

  /** A dark line around everything painted: the sprite's silhouette, as in hand-made iso art. */
  outline(color: string, min = 128) {
    const add: number[] = []
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        if (this.alpha(x, y) >= min) continue
        if (this.alpha(x - 1, y) >= min || this.alpha(x + 1, y) >= min || this.alpha(x, y - 1) >= min || this.alpha(x, y + 1) >= min) add.push(y * this.w + x)
      }
    }
    for (const i of add) this.set(i % this.w, Math.floor(i / this.w), color)
  }

  /** Flipped left to right: the same figure looking the other way. */
  flipped(): Painter {
    const p = new Painter(this.w, this.h)
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        const s = (y * this.w + x) * 4
        const d = (y * this.w + (this.w - 1 - x)) * 4
        for (let k = 0; k < 4; k++) p.data[d + k] = this.data[s + k]
      }
    }
    return p
  }

  canvas(): HTMLCanvasElement {
    return toCanvas(this.data, this.w, this.h)
  }
}

export function toCanvas(data: Uint8ClampedArray, w: number, h: number): HTMLCanvasElement {
  const c = document.createElement('canvas')
  c.width = w
  c.height = h
  const ctx = c.getContext('2d')!
  const image = ctx.createImageData(w, h)
  image.data.set(data)
  ctx.putImageData(image, 0, 0)
  return c
}

export function inside(points: Point[], x: number, y: number): boolean {
  let hit = false
  for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
    const [xi, yi] = points[i]
    const [xj, yj] = points[j]
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) hit = !hit
  }
  return hit
}
