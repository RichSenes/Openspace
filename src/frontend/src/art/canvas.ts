import type { Pixel } from './sprite'

const rgba = new Map<string, number>()

/** #rrggbb as a little-endian RGBA word, opaque. */
function packed(color: string): number {
  let value = rgba.get(color)
  if (value === undefined) {
    const n = parseInt(color.slice(1), 16)
    value = (0xff << 24) | ((n & 0xff) << 16) | (n & 0xff00) | ((n >> 16) & 0xff)
    rgba.set(color, value)
  }
  return value
}

/** Paints pixels on a canvas at 1x, replacing what it showed; scale it up with CSS or a texture, never smoothed. */
export function paintInto(canvas: HTMLCanvasElement, width: number, height: number, pixels: Pixel[]): HTMLCanvasElement {
  if (canvas.width !== width) canvas.width = width
  if (canvas.height !== height) canvas.height = height
  const ctx = canvas.getContext('2d')!
  const image = ctx.createImageData(width, height)
  const words = new Uint32Array(image.data.buffer)
  for (const p of pixels) {
    if (p.x >= 0 && p.x < width && p.y >= 0 && p.y < height) words[p.y * width + p.x] = packed(p.color)
  }
  ctx.putImageData(image, 0, 0)
  return canvas
}

/** Paints pixels on a fresh canvas at 1x. */
export const toCanvas = (width: number, height: number, pixels: Pixel[]) => paintInto(document.createElement('canvas'), width, height, pixels)
