import type { AgentState } from './agentStates'
import { poseOf } from './agentStates'
import { FURNITURE } from './fanPack'
import { inTheDark } from './lighting'
import { sceneLights, scenePixels, type SceneItem } from './scene'
import type { PropName } from './sheets/props'
import type { Signal } from './sheets/signals'
import { pixelsOf, type Pixel, type SpriteDef } from './sprite'
import { STATION, stationLights, stationPixels, themeColors, type Appearance } from './station'
import type { Theme } from './themes'

/** Floor tiles are this many pixels wide; walls are two tiles tall. */
export const TILE = 16
export const WALL_HEIGHT = 2 * TILE

/** Tile variants: the frame of a floor or wall tile depends on where it lies. */
const variant = (sprite: SpriteDef, c: number, r: number) => (c * 7 + r * 3) % sprite.frames.length

/**
 * The upper half of a wall: the tile's cap and panel without its skirting, so walls are
 * two tiles tall and leave room for what hangs on them.
 */
function upperWall(sprite: SpriteDef): SpriteDef {
  return { ...sprite, frames: sprite.frames.map((rows) => [...rows.slice(0, 10), ...rows.slice(2, 8)]) }
}

/** The back wall and the floor of a room `cols` tiles wide with `rows` tiles of floor. */
export function roomShell(theme: Theme, cols: number, rows: number): Pixel[] {
  const colors = themeColors(theme.colors)
  const upper = upperWall(theme.wall)
  const pixels: Pixel[] = []
  for (let c = 0; c < cols; c++) {
    pixels.push(...pixelsOf(upper, variant(upper, c, 0), colors, c * TILE, 0))
    pixels.push(...pixelsOf(theme.wall, variant(theme.wall, c, 1), colors, c * TILE, TILE))
  }
  for (let r = 0; r < rows; r++)
    for (let c = 0; c < cols; c++) pixels.push(...pixelsOf(theme.floor, variant(theme.floor, c, r), colors, c * TILE, WALL_HEIGHT + r * TILE))
  return pixels
}

/** A piece of furniture standing at (x, y), kept inside a room of the given size. */
export function propItem(name: PropName, x: number, y: number, tick: number, theme: Theme, room: { width: number; height: number }): SceneItem {
  const { sprite, lights, layer } = FURNITURE[name]
  const colors = themeColors(theme.colors)
  return {
    // A redrawn prop may be bigger than the one the layout was made for: keep it inside.
    x: Math.max(0, Math.min(x, room.width - sprite.width)),
    y: Math.max(0, Math.min(y, room.height - sprite.height)),
    height: sprite.height,
    pixels: pixelsOf(sprite, Math.floor(tick / 2), colors),
    lights: (lights ?? []).map((l) => ({ ...l, color: colors(l.color) ?? '#ffffff' })),
    layer,
  }
}

/** A desk with its agent at (x, y), in a state, at a tick of the animation clock. */
export function deskItem(look: Appearance, state: AgentState, x: number, y: number, tick: number, theme: Theme, signal?: Signal): SceneItem {
  const pose = poseOf(state, tick, signal)
  return { x, y, height: STATION.height, pixels: stationPixels(look, pose, theme), lights: stationLights(pose, theme) }
}

/** A whole room: shell, then everything in depth order, then the lights, if they are off. */
export function roomPixels(theme: Theme, shell: Pixel[], items: SceneItem[], dark: boolean): Pixel[] {
  const pixels = [...shell, ...scenePixels(items)]
  return dark ? inTheDark(pixels, sceneLights(items), theme.colors.night) : pixels
}
