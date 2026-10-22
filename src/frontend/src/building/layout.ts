import type { OfficeView, RoomView } from '../api/types'
import { FURNITURE } from '../art/fanPack'
import { themeOf } from '../art/themes'
import { TILE } from '../art/roomScene'
import { STATION } from '../art/station'
import type { PropName } from '../art/sheets/props'

export { TILE }

const PER_ROW = 4 // desks side by side before a new row starts
const SLOT = 3 // tiles of floor per desk, side by side and front to back
const MIN_INNER = 8 // an empty room is still a room
export const WALL = 2 // tiles of back wall
const HALL = 3 // tiles of corridor in front of each row of rooms
const OUT = 1 // tiles of ground around the building
const DOOR = 2 // tiles of doorway in a room's front wall
const HALL_SPACING = 6 // tiles between things standing in a corridor

/** A desk, in world pixels: where its station's top left corner goes. */
export interface DeskSpot {
  id: string
  name: string
  x: number
  y: number
}

/** A piece of furniture, in pixels from the room's top left corner. */
export interface PropSpot {
  name: PropName
  x: number
  y: number
}

/** A room, in tiles. */
export interface RoomBox {
  id: string
  name: string
  x: number
  y: number
  width: number
  height: number
  desks: DeskSpot[]
  props: PropSpot[]
}

/** A gap in a room's front wall onto the corridor, in world pixels. */
export interface Door {
  roomId: string
  x: number
  y: number
  width: number
}

/** The building: rooms joined wall to wall, with corridors in front of each row. */
export interface BuildingLayout {
  rooms: RoomBox[]
  /** The building's walls, in tiles: everything inside that is not a room is common floor. */
  floor: { x: number; y: number; width: number; height: number }
  doors: Door[]
  /** What stands in the corridors, in world pixels. */
  hall: PropSpot[]
  /** The whole picture, ground around the building included, in tiles. */
  width: number
  height: number
}

export const isProp = (name: string): name is PropName => Object.hasOwn(FURNITURE, name)

const overlaps = (a: { x: number; y: number }, b: { x: number; y: number }) =>
  Math.abs(a.x - b.x) < STATION.width && Math.abs(a.y - b.y) < STATION.height - 12

/**
 * Where each desk of a room stands, in pixels from the room's corner. Desks placed by
 * hand keep their spot; the others take the free slots of rows of four, back to front.
 */
function deskSpots(room: RoomView, firstColumn: number): Map<string, { x: number; y: number }> {
  const spots = new Map<string, { x: number; y: number }>()
  for (const desk of room.desks) if (desk.spot) spots.set(desk.id, desk.spot)
  const placed = [...spots.values()]
  let slot = 0
  for (const desk of room.desks) {
    if (desk.spot) continue
    let spot
    do {
      spot = {
        // Centred in its slot; the first row leans on the wall, heads in front of it.
        x: (firstColumn + (slot % PER_ROW) * SLOT) * TILE + (SLOT * TILE - STATION.width) / 2,
        y: WALL * TILE - 12 + Math.floor(slot / PER_ROW) * SLOT * TILE,
      }
      slot++
    } while (placed.some((p) => overlaps(p, spot!)))
    spots.set(desk.id, spot)
  }
  return spots
}

/** A room's size in tiles: as arranged, or grown with its desks. */
function sizeOf(room: RoomView): { width: number; height: number; firstColumn: number } {
  if (room.width && room.depth) return { width: room.width, height: WALL + room.depth, firstColumn: 1 }
  const columns = Math.min(PER_ROW, room.desks.length)
  const deskRows = Math.max(1, Math.ceil(room.desks.length / PER_ROW))
  const inner = Math.max(MIN_INNER, columns * SLOT)
  return { width: inner + 2, height: WALL + deskRows * SLOT + 1, firstColumn: 1 + Math.floor((inner - columns * SLOT) / 2) }
}

/**
 * Lays the office out as one building, in tiles: rooms side by side sharing their walls,
 * wrapping into a new row when a row would exceed maxWidth, and a corridor in front of
 * each row that every room opens onto. Rooms arranged by hand keep their size, desks and
 * furniture; the others grow with their desks and get a plant in their front corner.
 */
export function layoutOffice(office: OfficeView, maxWidth: number): BuildingLayout {
  const rooms: RoomBox[] = []
  const rows: { y: number; height: number; rooms: RoomBox[] }[] = []
  let x = OUT
  let y = OUT
  let row: (typeof rows)[number] | undefined

  for (const room of office.rooms) {
    const { width: roomWidth, height: roomHeight, firstColumn } = sizeOf(room)
    if (row && x > OUT && x + roomWidth + OUT > maxWidth) {
      x = OUT
      y += row.height + HALL
      row = undefined
    }
    if (!row) {
      row = { y, height: 0, rooms: [] }
      rows.push(row)
    }

    const spots = deskSpots(room, firstColumn)
    const arranged = Boolean(room.width && room.depth)
    const box: RoomBox = {
      id: room.id,
      name: room.name,
      x,
      y,
      width: roomWidth,
      height: roomHeight,
      desks: room.desks.map((d) => ({ id: d.id, name: d.name, x: x * TILE + spots.get(d.id)!.x, y: y * TILE + spots.get(d.id)!.y })),
      props: arranged
        ? (room.furniture ?? []).filter((p) => isProp(p.name)).map((p) => ({ ...p, name: p.name as PropName }))
        : [{ name: 'plant', x: roomWidth * TILE - 18, y: roomHeight * TILE - 26 }],
    }
    rooms.push(box)
    row.rooms.push(box)
    row.height = Math.max(row.height, roomHeight)
    x += roomWidth
  }

  if (rooms.length === 0) return { rooms, floor: { x: 0, y: 0, width: 0, height: 0 }, doors: [], hall: [], width: 0, height: 0 }

  const inside = Math.max(...rooms.map((r) => r.x + r.width)) - OUT
  const last = rows[rows.length - 1]
  const floor = { x: OUT, y: OUT, width: inside, height: last.y + last.height + HALL - OUT }
  const doors = rooms.map((r) => ({
    roomId: r.id,
    x: (r.x + Math.floor((r.width - DOOR) / 2)) * TILE,
    y: (r.y + r.height) * TILE,
    width: DOOR * TILE,
  }))
  const hall = rows.flatMap((r) => hallFurniture(office.theme, r.y + r.height, floor, doors.filter((d) => r.rooms.some((b) => b.id === d.roomId))))
  return { rooms, floor, doors, hall, width: floor.width + 2 * OUT, height: floor.height + 2 * OUT }
}

/**
 * The theme's corridor furniture, in turn, against the rooms of a row, every few
 * tiles and never in front of one of their doors.
 */
function hallFurniture(theme: string, rowBottom: number, floor: BuildingLayout['floor'], doors: Door[]): PropSpot[] {
  const names = themeOf(theme).hall.filter(isProp)
  const spots: PropSpot[] = []
  const top = rowBottom * TILE
  let x = floor.x * TILE + 6
  const end = (floor.x + floor.width) * TILE - 4
  while (names.length > 0 && x < end) {
    const name = names[spots.length % names.length]
    const { sprite } = FURNITURE[name]
    const blocked = doors.some((d) => x < d.x + d.width + 4 && x + sprite.width > d.x - 4)
    if (x + sprite.width > end) break
    if (blocked) {
      x += TILE / 2
      continue
    }
    // Their feet a little into the corridor, so people can walk in front of them.
    spots.push({ name, x, y: top + 30 - sprite.height })
    x += sprite.width + HALL_SPACING * TILE
  }
  return spots
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
