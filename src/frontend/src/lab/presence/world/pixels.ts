/** Small tools every world draws with: dithering, palettes, noise, tiny glyphs. */

const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5]

/** The ordered-dither threshold at a pixel, 0..1. */
export const bayerAt = (x: number, y: number) => (BAYER[(y & 3) * 4 + (x & 3)] + 0.5) / 16

export function hex(c: string): [number, number, number] {
  const n = parseInt(c.slice(1), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

/** A vertical gradient through a palette, dithered, written into an image. */
export function ditherBands(img: ImageData, colours: string[], curve = 1.7, from = 0, to = img.height) {
  const cols = colours.map(hex)
  const { width } = img
  for (let y = from; y < to; y++) {
    const f = ((y - from) / Math.max(1, to - from)) ** curve * (cols.length - 1)
    const i = Math.floor(f)
    const k = f - i
    for (let x = 0; x < width; x++) {
      const c = cols[Math.min(cols.length - 1, k > bayerAt(x, y) ? i + 1 : i)]
      const o = (y * width + x) * 4
      img.data[o] = c[0]
      img.data[o + 1] = c[1]
      img.data[o + 2] = c[2]
      img.data[o + 3] = 255
    }
  }
}

/** Smooth value noise in 2D, 0..1, repeatable from a seed. */
export function valueNoise(seed: number): (x: number, y: number) => number {
  const hash = (x: number, y: number) => {
    let h = (x * 374761393 + y * 668265263 + seed * 2147483647) | 0
    h = Math.imul(h ^ (h >>> 13), 1274126177)
    return ((h ^ (h >>> 16)) >>> 0) / 4294967296
  }
  return (x, y) => {
    const xi = Math.floor(x)
    const yi = Math.floor(y)
    const fx = x - xi
    const fy = y - yi
    const sx = fx * fx * (3 - 2 * fx)
    const sy = fy * fy * (3 - 2 * fy)
    const a = hash(xi, yi)
    const b = hash(xi + 1, yi)
    const c = hash(xi, yi + 1)
    const d = hash(xi + 1, yi + 1)
    return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy
  }
}

/** Fractal noise: a few octaves of value noise, 0..1. */
export function fbm(noise: (x: number, y: number) => number, x: number, y: number, octaves = 4): number {
  let sum = 0
  let amp = 0.5
  let norm = 0
  let f = 1
  for (let i = 0; i < octaves; i++) {
    sum += noise(x * f, y * f) * amp
    norm += amp
    amp *= 0.5
    f *= 2
  }
  return sum / norm
}

/** Draws a bitmap glyph ('#' is lit) at x, y. */
export function glyph(g: CanvasRenderingContext2D, rows: string[], x: number, y: number) {
  rows.forEach((row, dy) => {
    for (let dx = 0; dx < row.length; dx++) if (row[dx] === '#') g.fillRect(x + dx, y + dy, 1, 1)
  })
}

/** 3×5 digits for code that falls and drifts. */
export const BITS: Record<'0' | '1', string[]> = {
  '0': ['###', '#.#', '#.#', '#.#', '###'],
  '1': ['.#.', '##.', '.#.', '.#.', '###'],
}

/** A filled disc of pixels, dithered at its edge when `soft`. */
export function disc(g: CanvasRenderingContext2D, cx: number, cy: number, r: number, soft = 0) {
  for (let dy = -Math.ceil(r + soft); dy <= r + soft; dy++)
    for (let dx = -Math.ceil(r + soft); dx <= r + soft; dx++) {
      const d = Math.hypot(dx, dy)
      if (d <= r || (soft > 0 && d <= r + soft && bayerAt(cx + dx, cy + dy) < 1 - (d - r) / soft)) g.fillRect(Math.round(cx + dx), Math.round(cy + dy), 1, 1)
    }
}

/** A cheap repeatable 0..1 from an integer. */
export const hash1 = (n: number) => {
  const s = Math.sin(n * 127.1 + 311.7) * 43758.5453
  return s - Math.floor(s)
}

/**
 * Layers that cost a pixel loop are drawn once into a canvas and reused: the cache keeps one
 * canvas per key and redraws it only when the key changes.
 */
export class LayerCache {
  private layers = new Map<string, HTMLCanvasElement>()
  get(name: string, key: string, w: number, h: number, draw: (g: CanvasRenderingContext2D) => void): HTMLCanvasElement {
    const id = `${name}|${key}|${w}x${h}`
    let c = this.layers.get(id)
    if (!c) {
      for (const k of [...this.layers.keys()]) if (k.startsWith(`${name}|`)) this.layers.delete(k)
      c = document.createElement('canvas')
      c.width = w
      c.height = h
      draw(c.getContext('2d')!)
      this.layers.set(id, c)
    }
    return c
  }
}

/** A whole number of waves across `w`, close to the frequency asked: so a layer tiles seamlessly. */
export const wavesAcross = (w: number, freq: number) => (2 * Math.PI * Math.max(1, Math.round((freq * w) / (2 * Math.PI)))) / w
