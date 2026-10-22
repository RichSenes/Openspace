import type { Pixel } from './sprite'

/** Something standing in a room: its own pixels, where its corner is and how tall it is. */
export interface SceneItem {
  x: number
  y: number
  height: number
  pixels: Pixel[]
}

/** Where an item touches the floor. */
export const baseline = (item: SceneItem) => item.y + item.height

/**
 * The pixels of a room's items in depth order: the lower an item stands on the floor,
 * the closer it is, so it is drawn later and covers what is behind it.
 */
export function scenePixels(items: SceneItem[]): Pixel[] {
  return [...items]
    .sort((a, b) => baseline(a) - baseline(b))
    .flatMap((item) => item.pixels.map((p) => ({ ...p, x: p.x + item.x, y: p.y + item.y })))
}
