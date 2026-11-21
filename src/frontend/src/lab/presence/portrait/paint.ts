/**
 * Painting at the art's own size: each layer is drawn with vector shapes into a scratch canvas,
 * then every pixel is snapped to the layer's colour ramp with an ordered dither and the soft
 * edges are cut hard. What comes out is pixel art: whole pixels, a few tones per material.
 */
import { bayerAt, hex } from '../world/pixels'

export type Ramp = string[]

/** Luminance of an RGB colour, 0..255. */
const lum = (r: number, g: number, b: number) => 0.299 * r + 0.587 * g + 0.114 * b

/**
 * Snaps a painted layer onto a ramp (dark to light) by brightness, dithered between neighbours,
 * and keeps only pixels at least half covered.
 */
export function quantize(img: ImageData, ramp: Ramp, offset: [number, number] = [0, 0]) {
  const cols = ramp.map(hex)
  const lums = cols.map((c) => lum(...c))
  const { data, width } = img
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] < 128) {
      data[i + 3] = 0
      continue
    }
    const p = i / 4
    const x = (p % width) + offset[0]
    const y = Math.floor(p / width) + offset[1]
    const l = lum(data[i], data[i + 1], data[i + 2])
    let k = 0
    while (k < lums.length - 1 && lums[k + 1] <= l) k++
    let c = cols[k]
    if (k < lums.length - 1) {
      // Dither only in a narrow band between two tones: clusters of flat colour, not a checkerboard.
      const f = Math.min(1, Math.max(0, ((l - lums[k]) / Math.max(1, lums[k + 1] - lums[k]) - 0.5) * 3 + 0.5))
      if (f > bayerAt(x, y)) c = cols[k + 1]
    }
    data[i] = c[0]
    data[i + 1] = c[1]
    data[i + 2] = c[2]
    data[i + 3] = 255
  }
}

/** A layer: drawn with full-colour vector shapes, then snapped to its ramp. */
let scratch: CanvasRenderingContext2D | null = null

export function layer(target: CanvasRenderingContext2D, ramp: Ramp, draw: (g: CanvasRenderingContext2D) => void) {
  const { width, height } = target.canvas
  // One scratch canvas, reused: the portrait is redrawn many times a second.
  if (!scratch || scratch.canvas.width !== width || scratch.canvas.height !== height) {
    const c = document.createElement('canvas')
    c.width = width
    c.height = height
    scratch = c.getContext('2d', { willReadFrequently: true })!
  }
  const g = scratch
  const c = g.canvas
  g.setTransform(1, 0, 0, 1, 0, 0)
  g.globalCompositeOperation = 'source-over'
  g.clearRect(0, 0, width, height)
  draw(g)
  const img = g.getImageData(0, 0, width, height)
  quantize(img, ramp)
  g.putImageData(img, 0, 0)
  target.drawImage(c, 0, 0)
}

/** A one-pixel outline in `colour` around everything already on the canvas. */
export function outline(target: CanvasRenderingContext2D, colour: string) {
  const { width, height } = target.canvas
  const img = target.getImageData(0, 0, width, height)
  const d = img.data
  const solid = (x: number, y: number) => x >= 0 && y >= 0 && x < width && y < height && d[(y * width + x) * 4 + 3] > 0
  const [r, g, b] = hex(colour)
  const out = new Uint8ClampedArray(d)
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++) {
      if (solid(x, y)) continue
      if (solid(x - 1, y) || solid(x + 1, y) || solid(x, y - 1) || solid(x, y + 1)) {
        const o = (y * width + x) * 4
        out[o] = r
        out[o + 1] = g
        out[o + 2] = b
        out[o + 3] = 255
      }
    }
  img.data.set(out)
  target.putImageData(img, 0, 0)
}

/** Hard single pixels, for lines that must stay one pixel wide (lids, lashes, lips). */
export function px(g: CanvasRenderingContext2D, colour: string, points: [number, number][]) {
  g.fillStyle = colour
  for (const [x, y] of points) g.fillRect(Math.round(x), Math.round(y), 1, 1)
}

/** The pixels of a curve, one per step along x, without gaps. */
export function curve(fn: (x: number) => number, from: number, to: number): [number, number][] {
  const out: [number, number][] = []
  let prev: number | null = null
  for (let x = Math.round(from); x <= Math.round(to); x++) {
    const y = Math.round(fn(x))
    if (prev !== null && Math.abs(y - prev) > 1) {
      const step = y > prev ? 1 : -1
      for (let k: number = prev + step; k !== y; k += step) out.push([x, k])
    }
    out.push([x, y])
    prev = y
  }
  return out
}
