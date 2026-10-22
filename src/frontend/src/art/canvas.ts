import type { Pixel } from './sprite'

/** Paints pixels on a fresh canvas at 1x; scale it up with CSS or a texture, never smoothed. */
export function toCanvas(width: number, height: number, pixels: Pixel[]): HTMLCanvasElement {
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const ctx = canvas.getContext('2d')!
  for (const p of pixels) {
    ctx.fillStyle = p.color
    ctx.fillRect(p.x, p.y, 1, 1)
  }
  return canvas
}
