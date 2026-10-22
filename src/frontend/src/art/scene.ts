import type { Light } from './lighting'
import type { Pixel } from './sprite'

/** Something standing in a room: its own pixels, where its corner is and how tall it is. */
export interface SceneItem {
  x: number
  y: number
  height: number
  pixels: Pixel[]
  /** Lights it gives off, in its own pixels. */
  lights?: Light[]
  /** Rugs lie on the floor and masks hang on walls: under everything that stands. */
  layer?: 'floor' | 'wall'
}

const LAYERS = { floor: 0, wall: 1, standing: 2 }
const order = (item: SceneItem) => LAYERS[item.layer ?? 'standing']

/** Where an item touches the floor. */
export const baseline = (item: SceneItem) => item.y + item.height

/**
 * The pixels of a room's items in depth order: rugs, then what hangs on walls, then what
 * stands, from the back. The lower an item stands on the floor, the closer it is, so it
 * is drawn later and covers what is behind it.
 */
export function scenePixels(items: SceneItem[]): Pixel[] {
  return [...items]
    .sort((a, b) => order(a) - order(b) || baseline(a) - baseline(b))
    .flatMap((item) => item.pixels.map((p) => ({ ...p, x: p.x + item.x, y: p.y + item.y })))
}

/** Every light of a room's items, moved to where each item stands. */
export const sceneLights = (items: SceneItem[]): Light[] =>
  items.flatMap((item) => (item.lights ?? []).map((l) => ({ ...l, x: l.x + item.x, y: l.y + item.y })))
