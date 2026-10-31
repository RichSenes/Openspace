import type { AgentState } from '../art/agentStates'
import type { ThemeColors } from '../art/palette'
import { STATION } from '../art/station'
import type { Pixel } from '../art/sprite'
import { TILE, WALL_HEIGHT } from '../art/roomScene'
import type { Camera, Rect, Size } from './camera'
import { inBuilding, type BuildingLayout } from './layout'

/** Pixels of the map for each tile of the building: a sketch, not a copy. */
export const MAP_TILE = 2

/** The most room the map takes on screen, in CSS pixels; it is drawn larger only by whole steps. */
export const MAP_BOX: Size = { width: 200, height: 140 }

/** How big the map is in its own pixels, and by how much it is enlarged on screen. */
export function mapSize(layout: Pick<BuildingLayout, 'width' | 'height'>): Size & { zoom: number } {
  const width = layout.width * MAP_TILE
  const height = layout.height * MAP_TILE
  const zoom = Math.max(1, Math.min(4, Math.floor(Math.min(MAP_BOX.width / width, MAP_BOX.height / height))))
  return { width, height, zoom }
}

/**
 * Marks for agents, three map pixels across, so a state reads from its shape as well as its
 * colour: a diamond at work, a ring in the queue, an exclamation mark when it needs you, a
 * cross when it failed. `#` is the mark, the rest is left alone; every mark gets a dark rim.
 */
const MARKS: Record<AgentState, { rows: string[]; color: string; blink?: true }> = {
  working: { rows: ['.#.', '###', '.#.'], color: '#7ee08a' },
  reading: { rows: ['.#.', '###', '.#.'], color: '#8fb8ff' },
  waiting: { rows: ['.#.', '.#.', '...', '.#.'], color: '#f2b84b', blink: true },
  queued: { rows: ['###', '#.#', '###'], color: '#a3a8b6' },
  idle: { rows: ['...', '.#.', '...'], color: '#a3a8b6' },
  done: { rows: ['##', '##'], color: '#ece8df' },
  failed: { rows: ['#.#', '.#.', '#.#'], color: '#ff6b6b' },
}

const RIM = '#0b0c10'

/** Colours of a room on the map, from its theme: floor, back wall and outline; the corridors take the wall's grey. */
export interface RoomInk {
  floor: string
  wall: string
  outline: string
}

export const inkOf = (colors: ThemeColors): RoomInk => ({ floor: colors['floor.mid'], wall: colors['wall.top'], outline: colors.outline })

/** One agent on the map: where its desk stands, in world pixels, and what it is up to. */
export interface MapAgent {
  x: number
  y: number
  state: AgentState
}

/**
 * The building as a little map: common floor in the office's colour, each room a block in
 * its own with its back wall along the top, and agents as marks. Working agents pulse and an
 * agent that needs you blinks, both on `tick`.
 */
export function minimapPixels(layout: BuildingLayout, hall: RoomInk, rooms: ReadonlyMap<string, RoomInk>, agents: readonly MapAgent[], tick: number): Pixel[] {
  const { width, height } = mapSize(layout)
  const grid = new Map<number, string>()
  const set = (x: number, y: number, color: string) => {
    if (x >= 0 && y >= 0 && x < width && y < height) grid.set(y * width + x, color)
  }
  const block = (tx: number, ty: number, color: string) => {
    for (let dy = 0; dy < MAP_TILE; dy++) for (let dx = 0; dx < MAP_TILE; dx++) set(tx * MAP_TILE + dx, ty * MAP_TILE + dy, color)
  }

  for (let ty = 0; ty < layout.height; ty++) for (let tx = 0; tx < layout.width; tx++) if (inBuilding(layout, tx, ty)) block(tx, ty, hall.floor)
  // The building's own edge, cuts included, so its shape reads at a glance.
  const inside = (x: number, y: number) => inBuilding(layout, Math.floor(x / MAP_TILE), Math.floor(y / MAP_TILE))
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++)
      if (inside(x, y) && (!inside(x + 1, y) || !inside(x - 1, y) || !inside(x, y + 1) || !inside(x, y - 1))) set(x, y, hall.outline)

  const wallRows = WALL_HEIGHT / TILE
  for (const room of layout.rooms) {
    const ink = rooms.get(room.id) ?? hall
    for (let ty = room.y; ty < room.y + room.height; ty++)
      for (let tx = room.x; tx < room.x + room.width; tx++) block(tx, ty, ty < room.y + wallRows ? ink.wall : ink.floor)
    const [left, top] = [room.x * MAP_TILE, room.y * MAP_TILE]
    const [right, bottom] = [(room.x + room.width) * MAP_TILE - 1, (room.y + room.height) * MAP_TILE - 1]
    for (let x = left; x <= right; x++) {
      set(x, top, ink.outline)
      set(x, bottom, ink.outline)
    }
    for (let y = top; y <= bottom; y++) {
      set(left, y, ink.outline)
      set(right, y, ink.outline)
    }
  }

  for (const agent of agents) {
    const mark = MARKS[agent.state]
    if (mark.blink && tick % 4 >= 2) continue
    const pulse = agent.state === 'working' && tick % 6 >= 3
    const color = pulse ? '#c4f5cb' : mark.color
    const cx = Math.round(((agent.x + STATION.width / 2) / TILE) * MAP_TILE)
    const cy = Math.round(((agent.y + STATION.height / 2) / TILE) * MAP_TILE)
    const x0 = cx - Math.floor(mark.rows[0].length / 2)
    const y0 = cy - Math.floor(mark.rows.length / 2)
    const lit: [number, number][] = []
    mark.rows.forEach((row, y) => [...row].forEach((c, x) => c === '#' && lit.push([x0 + x, y0 + y])))
    for (const [x, y] of lit) for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) set(x + dx, y + dy, RIM)
    for (const [x, y] of lit) set(x, y, color)
  }

  return [...grid.entries()].map(([i, color]) => ({ x: i % width, y: Math.floor(i / width), color }))
}

/** The part of the world the screen shows, in map pixels. */
export function mapView(camera: Camera, screen: Size): Rect {
  const scale = MAP_TILE / TILE / camera.scale
  return { x: -camera.x * scale, y: -camera.y * scale, width: screen.width * scale, height: screen.height * scale }
}

/** The camera that centres the screen on a point of the map, at the same zoom. */
export function centredOn(camera: Camera, screen: Size, mapX: number, mapY: number): Camera {
  const worldX = (mapX / MAP_TILE) * TILE
  const worldY = (mapY / MAP_TILE) * TILE
  return { scale: camera.scale, x: screen.width / 2 - worldX * camera.scale, y: screen.height / 2 - worldY * camera.scale }
}
