/** Where the world sits on screen: screen = world * scale + (x, y). */
export interface Camera {
  x: number
  y: number
  scale: number
}

export interface Size {
  width: number
  height: number
}

export interface Rect extends Size {
  x: number
  y: number
}

export const MIN_SCALE = 0.3
export const MAX_SCALE = 6

const clampScale = (scale: number) => Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale))

/**
 * The camera that shows a world rectangle whole and centred. Whole-number zoom keeps
 * pixel art crisp, so it is preferred whenever the content fits at 1x or more.
 */
export function fit(target: Rect, viewport: Size, margin = 32): Camera {
  if (target.width === 0 || target.height === 0) return { x: 0, y: 0, scale: 1 }
  const exact = Math.min((viewport.width - margin) / target.width, (viewport.height - margin) / target.height)
  const scale = clampScale(exact >= 1 ? Math.min(4, Math.floor(exact)) : exact)
  return {
    scale,
    x: Math.round((viewport.width - target.width * scale) / 2 - target.x * scale),
    y: Math.round((viewport.height - target.height * scale) / 2 - target.y * scale),
  }
}

/** Zooms by a factor keeping the world point under `screen` exactly where it is. */
export function zoomAt(camera: Camera, factor: number, screen: { x: number; y: number }): Camera {
  const scale = clampScale(camera.scale * factor)
  const worldX = (screen.x - camera.x) / camera.scale
  const worldY = (screen.y - camera.y) / camera.scale
  return { scale, x: screen.x - worldX * scale, y: screen.y - worldY * scale }
}

export const pan = (camera: Camera, dx: number, dy: number): Camera => ({ ...camera, x: camera.x + dx, y: camera.y + dy })

export function toWorld(camera: Camera, screen: { x: number; y: number }): { x: number; y: number } {
  return { x: (screen.x - camera.x) / camera.scale, y: (screen.y - camera.y) / camera.scale }
}

/** A step of a smooth move from one camera to another, t from 0 to 1 (ease-out). */
export function between(from: Camera, to: Camera, t: number): Camera {
  const e = 1 - Math.pow(1 - Math.min(1, Math.max(0, t)), 3)
  return {
    x: from.x + (to.x - from.x) * e,
    y: from.y + (to.y - from.y) * e,
    scale: from.scale + (to.scale - from.scale) * e,
  }
}

/** One wheel notch zooms by about 15%, whatever the device reports. */
export const wheelFactor = (deltaY: number) => Math.exp(-Math.max(-100, Math.min(100, deltaY)) * 0.0015)
