import type { OfficeView } from '../api/types'

/** Size of a floor tile in world pixels. Everything is measured in tiles. */
export const TILE = 16

const DESK_SLOT = 4 // tiles of floor per desk, side by side
const ROOM_HEIGHT = 9 // tiles, walls included
const MIN_INNER_WIDTH = 8 // an empty room is still a room
const GAP = 2 // tiles of corridor between rooms

export interface DeskSpot {
  id: string
  name: string
  x: number
  y: number
}

export interface RoomBox {
  id: string
  name: string
  x: number
  y: number
  width: number
  height: number
  desks: DeskSpot[]
}

export interface BuildingLayout {
  rooms: RoomBox[]
  width: number
  height: number
}

/**
 * Lays the rooms of an office out like a floor plan, in tiles: left to right,
 * wrapping when a row would exceed maxWidth. Rooms grow with their desks.
 */
export function layoutOffice(office: OfficeView, maxWidth: number): BuildingLayout {
  const rooms: RoomBox[] = []
  let x = GAP
  let y = GAP
  let width = 0

  for (const room of office.rooms) {
    const inner = Math.max(MIN_INNER_WIDTH, room.desks.length * DESK_SLOT)
    const roomWidth = inner + 2
    if (x > GAP && x + roomWidth + GAP > maxWidth) {
      x = GAP
      y += ROOM_HEIGHT + GAP
    }

    const firstDesk = x + 1 + Math.floor((inner - room.desks.length * DESK_SLOT) / 2)
    rooms.push({
      id: room.id,
      name: room.name,
      x,
      y,
      width: roomWidth,
      height: ROOM_HEIGHT,
      desks: room.desks.map((d, i) => ({ id: d.id, name: d.name, x: firstDesk + i * DESK_SLOT + 1, y: y + 4 })),
    })

    x += roomWidth + GAP
    width = Math.max(width, x)
  }

  return { rooms, width, height: rooms.length === 0 ? 0 : y + ROOM_HEIGHT + GAP }
}

/** Largest whole-number zoom that fits, so pixel art stays crisp; below 1 only when it must. */
export function fitScale(content: { width: number; height: number }, viewport: { width: number; height: number }): number {
  if (content.width === 0 || content.height === 0) return 1
  const exact = Math.min(viewport.width / content.width, viewport.height / content.height)
  return exact >= 1 ? Math.min(4, Math.floor(exact)) : exact
}

/**
 * Tries every row width from "all rooms in one row" down to "one room per row"
 * and keeps the arrangement that can be drawn biggest in the viewport.
 */
export function bestLayout(
  office: OfficeView,
  viewport: { width: number; height: number },
): { layout: BuildingLayout; scale: number } {
  const single = layoutOffice(office, Number.MAX_SAFE_INTEGER)
  let best = { layout: single, scale: fitScale(pixels(single), viewport) }

  for (let maxWidth = single.width - 1; maxWidth > 0; maxWidth--) {
    const layout = layoutOffice(office, maxWidth)
    if (layout.width >= best.layout.width && layout.height === best.layout.height) continue
    const scale = fitScale(pixels(layout), viewport)
    if (scale > best.scale) best = { layout, scale }
    if (layout.rooms.every((r) => r.x === layout.rooms[0].x)) break
  }

  return best
}

const pixels = (layout: BuildingLayout) => ({ width: layout.width * TILE, height: layout.height * TILE })
