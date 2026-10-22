import type { DoorPlacement, OfficeView, RoomView, WallSide } from '../api/types'
import { FURNITURE } from '../art/fanPack'
import { TILE } from '../art/roomScene'
import type { PropName } from '../art/sheets/props'
import { STATION } from '../art/station'
import { themeOf } from '../art/themes'

export { TILE }

const PER_ROW = 4 // desks side by side before a new row starts
const SLOT = 3 // tiles of floor per desk, side by side and front to back
const MIN_INNER = 8 // an empty room is still a room
export const WALL = 2 // tiles of back wall
const HALL = 3 // tiles of corridor in front of each row of rooms
export const OUT = 1 // tiles of ground around the building
export const DOOR = 2 // tiles of doorway
const HALL_SPACING = 6 // tiles between things standing in a corridor

/** A desk, in world pixels: where its station's top left corner goes. */
export interface DeskSpot {
  id: string
  name: string
  x: number
  y: number
}

/** A piece of furniture, in pixels from the top left corner of its room or of the building. */
export interface PropSpot {
  name: PropName
  x: number
  y: number
}

/** A doorway, in world pixels: where it starts on its wall and how long it is. */
export interface Door {
  roomId: string
  wall: WallSide
  x: number
  y: number
  length: number
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
  door: DoorPlacement
  /** Windows of the back wall, by the pixel where each starts from the room's left. */
  windows: number[]
}

/** The building: rooms joined wall to wall, and common floor everywhere else. */
export interface BuildingLayout {
  rooms: RoomBox[]
  /** The building's walls, in tiles: everything inside that is not a room is common floor. */
  floor: { x: number; y: number; width: number; height: number }
  doors: Door[]
  /** What stands on the common floor, in world pixels. */
  hall: PropSpot[]
  /** Whether the building was drawn by hand: then it never moves by itself. */
  planned: boolean
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
      spot = autoSlot(slot, firstColumn)
      slot++
    } while (placed.some((p) => overlaps(p, spot!)))
    spots.set(desk.id, spot)
  }
  return spots
}

/** The n-th desk slot of a room: rows of four, the first leaning on the wall, heads in front of it. */
export const autoSlot = (slot: number, firstColumn = 1) => ({
  x: (firstColumn + (slot % PER_ROW) * SLOT) * TILE + (SLOT * TILE - STATION.width) / 2,
  y: WALL * TILE - 12 + Math.floor(slot / PER_ROW) * SLOT * TILE,
})

/** A room's size in tiles: as arranged, or grown with its desks. */
function sizeOf(room: RoomView): { width: number; height: number; firstColumn: number } {
  if (room.width && room.depth) return { width: room.width, height: WALL + room.depth, firstColumn: 1 }
  const columns = Math.min(PER_ROW, room.desks.length)
  const deskRows = Math.max(1, Math.ceil(room.desks.length / PER_ROW))
  const inner = Math.max(MIN_INNER, columns * SLOT)
  return { width: inner + 2, height: WALL + deskRows * SLOT + 1, firstColumn: 1 + Math.floor((inner - columns * SLOT) / 2) }
}

/** The middle of the front wall, where a door goes unless it is moved. */
export const defaultDoor = (width: number): DoorPlacement => ({ wall: 'Front', offset: Math.floor((width - DOOR) / 2) })

/** A room's contents at a place, in tiles. */
function boxOf(room: RoomView, x: number, y: number): RoomBox {
  const { width, height, firstColumn } = sizeOf(room)
  const spots = deskSpots(room, firstColumn)
  const arranged = Boolean(room.width && room.depth)
  return {
    id: room.id,
    name: room.name,
    x,
    y,
    width,
    height,
    desks: room.desks.map((d) => ({ id: d.id, name: d.name, x: x * TILE + spots.get(d.id)!.x, y: y * TILE + spots.get(d.id)!.y })),
    props: arranged
      ? (room.furniture ?? []).filter((p) => isProp(p.name)).map((p) => ({ ...p, name: p.name as PropName }))
      : [{ name: 'plant', x: width * TILE - 18, y: height * TILE - 26 }],
    door: room.door ?? defaultDoor(width),
    windows: room.windows ?? [],
  }
}

/** Where a room's door opens, in world pixels. */
export function doorOf(room: RoomBox): Door {
  const { wall, offset } = room.door
  const along = (wall === 'Left' || wall === 'Right' ? WALL + offset : offset) * TILE
  const [left, top] = [room.x * TILE, room.y * TILE]
  const [right, bottom] = [left + room.width * TILE, top + room.height * TILE]
  const at = { Front: [left + along, bottom], Back: [left + along, top], Left: [left, top + along], Right: [right, top + along] }[wall]
  return { roomId: room.id, wall, x: at[0], y: at[1], length: DOOR * TILE }
}

/**
 * Lays the office out as one building, in tiles. Drawn by hand, it keeps its size and
 * every room stays where it was put; rooms without a place yet take the first free one.
 * Otherwise rooms stand side by side sharing their walls, in rows of about the square
 * root of their number, with a corridor in front of each row. Never the screen's size:
 * the building is the same on every screen.
 */
export function layoutOffice(office: OfficeView, perRow = Math.ceil(Math.sqrt(office.rooms.length))): BuildingLayout {
  return office.floorWidth && office.floorDepth ? plannedLayout(office) : automaticLayout(office, perRow)
}

function plannedLayout(office: OfficeView): BuildingLayout {
  const floor = { x: OUT, y: OUT, width: office.floorWidth!, height: office.floorDepth! }
  const rooms: RoomBox[] = []
  const waiting: RoomView[] = []
  for (const room of office.rooms) {
    if (room.left != null && room.top != null) rooms.push(boxOf(room, OUT + room.left, OUT + room.top))
    else waiting.push(room)
  }
  for (const room of waiting) {
    const { width, height } = sizeOf(room)
    const spot = freeSpot(rooms, floor, width, height) ?? { x: floor.x, y: floor.y + floor.height }
    floor.height = Math.max(floor.height, spot.y + height - floor.y)
    rooms.push(boxOf(room, spot.x, spot.y))
  }
  const hall = (office.furniture ?? [])
    .filter((p) => isProp(p.name))
    .map((p) => ({ name: p.name as PropName, x: p.x + floor.x * TILE, y: p.y + floor.y * TILE }))
  return { rooms, floor, doors: rooms.map(doorOf), hall, planned: true, width: floor.width + 2 * OUT, height: floor.height + 2 * OUT }
}

/** The first place on the floor, back to front and left to right, where a room fits. */
export function freeSpot(
  rooms: readonly { x: number; y: number; width: number; height: number }[],
  floor: BuildingLayout['floor'],
  width: number,
  height: number,
): { x: number; y: number } | undefined {
  for (let y = floor.y; y + height <= floor.y + floor.height; y++)
    for (let x = floor.x; x + width <= floor.x + floor.width; x++)
      if (!rooms.some((r) => x < r.x + r.width && r.x < x + width && y < r.y + r.height && r.y < y + height)) return { x, y }
  return undefined
}

function automaticLayout(office: OfficeView, perRow: number): BuildingLayout {
  const rooms: RoomBox[] = []
  const rows: { y: number; height: number; rooms: RoomBox[] }[] = []
  let y = OUT
  office.rooms.forEach((room, i) => {
    if (i % Math.max(1, perRow) === 0) {
      const previous = rows[rows.length - 1]
      if (previous) y += previous.height + HALL
      rows.push({ y, height: 0, rooms: [] })
    }
    const row = rows[rows.length - 1]
    const last = row.rooms[row.rooms.length - 1]
    const box = boxOf(room, last ? last.x + last.width : OUT, y)
    rooms.push(box)
    row.rooms.push(box)
    row.height = Math.max(row.height, box.height)
  })

  if (rooms.length === 0) return { rooms, floor: { x: 0, y: 0, width: 0, height: 0 }, doors: [], hall: [], planned: false, width: 0, height: 0 }

  const inside = Math.max(...rooms.map((r) => r.x + r.width)) - OUT
  const last = rows[rows.length - 1]
  const floor = { x: OUT, y: OUT, width: inside, height: last.y + last.height + HALL - OUT }
  const doors = rooms.map(doorOf)
  const hall = rows.flatMap((r) => hallFurniture(office.theme, r.y + r.height, floor, doors.filter((d) => r.rooms.some((b) => b.id === d.roomId))))
  return { rooms, floor, doors, hall, planned: false, width: floor.width + 2 * OUT, height: floor.height + 2 * OUT }
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
    const blocked = doors.some((d) => x < d.x + d.length + 4 && x + sprite.width > d.x - 4)
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
