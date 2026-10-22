import type { DoorPlacement, FloorPlanRequest, FurniturePiece, OfficeView, RoomLayoutRequest } from '../api/types'
import { FURNITURE } from '../art/fanPack'
import { WALL_HEIGHT } from '../art/roomScene'
import { PROPS, type PropName } from '../art/sheets/props'
import { STATION } from '../art/station'
import { autoSlot, DOOR, isProp, layoutOffice, TILE, WALL, type BuildingLayout, type RoomBox } from './layout'

/** A room being arranged: its size in tiles, its desks and furniture in pixels from its corner, its door and windows. */
export interface Arrangement {
  width: number
  depth: number
  desks: Record<string, { x: number; y: number }>
  furniture: FurniturePiece[]
  door: DoorPlacement
  windows: number[]
}

/** The building being drawn: its size in tiles, where rooms stand and the common floor's furniture, in pixels. */
export interface FloorArrangement {
  width: number
  depth: number
  furniture: FurniturePiece[]
  rooms: Record<string, { left: number; top: number }>
}

/** Changes not saved yet; `auto` gives the whole office back to the automatic layout. */
export interface Drafts {
  floor?: FloorArrangement
  rooms: Record<string, Arrangement>
  auto?: boolean
}

export const NO_DRAFTS: Drafts = { rooms: {} }

/** The selection key of the common floor, the building itself. */
export const BUILDING = 'building'

/** What can be picked up in a room, or on the common floor. */
export type Target = { kind: 'desk'; id: string } | { kind: 'piece'; index: number } | { kind: 'door' } | { kind: 'window'; index: number }

export interface Rect {
  x: number
  y: number
  width: number
  height: number
}

/** Sizes the editor offers, in tiles; the API allows a little more. */
export const LIMITS = { width: [4, 40], depth: [3, 30], floorWidth: [6, 120], floorDepth: [5, 80] } as const

/** A window in a back wall: its size and how far below the top of the wall it starts. */
export const WINDOW = { width: 24, height: 22, y: 5 } as const

/** Every piece of furniture, in the order of the sheet. */
export const FURNITURE_NAMES = Object.keys(PROPS) as PropName[]

/** "sideTable" reads "Side table". */
export const furnitureLabel = (name: string) => {
  const words = name.replace(/([A-Z])/g, ' $1').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

const clamp = (value: number, low: number, high: number) => Math.max(low, Math.min(high, value))

const intersects = (a: Rect, b: Rect) => a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height

/** The arrangement a room has now, as drawn: the starting point of an edit. */
export function arrangementOf(box: RoomBox): Arrangement {
  const left = box.x * TILE
  const top = box.y * TILE
  return {
    width: box.width,
    depth: box.height - WALL,
    desks: Object.fromEntries(box.desks.map((d) => [d.id, { x: d.x - left, y: d.y - top }])),
    furniture: box.props.map((p) => ({ ...p })),
    door: { ...box.door },
    windows: [...box.windows],
  }
}

/** The building as drawn now, in tiles from its corner and pixels for the furniture. */
export function floorArrangementOf(layout: BuildingLayout): FloorArrangement {
  const { floor } = layout
  return {
    width: floor.width,
    depth: floor.height,
    furniture: layout.hall.map((p) => ({ name: p.name, x: p.x - floor.x * TILE, y: p.y - floor.y * TILE })),
    rooms: Object.fromEntries(layout.rooms.map((r) => [r.id, { left: r.x - floor.x, top: r.y - floor.y }])),
  }
}

/** The office as it will look once the drafts are saved. */
export function withDrafts(office: OfficeView, drafts: Drafts): OfficeView {
  if (drafts.auto) {
    return {
      ...office,
      floorWidth: null,
      floorDepth: null,
      furniture: [],
      rooms: office.rooms.map((r) => ({ ...r, width: null, depth: null, furniture: [], door: null, windows: [], left: null, top: null, desks: r.desks.map((d) => ({ ...d, spot: null })) })),
    }
  }
  const floor = drafts.floor
  return {
    ...office,
    ...(floor && { floorWidth: floor.width, floorDepth: floor.depth, furniture: floor.furniture }),
    rooms: office.rooms.map((room) => {
      const draft = drafts.rooms[room.id]
      const place = floor?.rooms[room.id]
      return {
        ...room,
        ...(place && { left: place.left, top: place.top }),
        ...(draft && {
          width: draft.width,
          depth: draft.depth,
          furniture: draft.furniture,
          door: draft.door,
          windows: draft.windows,
          desks: room.desks.map((d) => ({ ...d, spot: draft.desks[d.id] ?? null })),
        }),
      }
    }),
  }
}

/** The arrangement a room has in the office as shown: its draft, or how it is drawn now. */
export function currentArrangement(shown: OfficeView, drafts: Drafts, roomId: string): Arrangement | undefined {
  const draft = drafts.rooms[roomId]
  if (draft) return draft
  const box = layoutOffice(shown).rooms.find((r) => r.id === roomId)
  return box && arrangementOf(box)
}

/** The building as shown: its draft, or how it is drawn now. */
export const currentFloor = (shown: OfficeView, drafts: Drafts): FloorArrangement => drafts.floor ?? floorArrangementOf(layoutOffice(shown))

type Sizes = Record<string, { width: number; height: number }>

/** The tiles each room takes, wall included, by id. */
export const roomSizes = (shown: OfficeView, drafts: Drafts): Sizes =>
  Object.fromEntries(
    layoutOffice(shown).rooms.map((r) => {
      const draft = drafts.rooms[r.id]
      return [r.id, draft ? { width: draft.width, height: draft.depth + WALL } : { width: r.width, height: r.height }]
    }),
  )

/** Whether a room of a size fits at a place: inside the building and on nobody else's tiles. */
export function fits(floor: FloorArrangement, sizes: Sizes, id: string, left: number, top: number, width: number, height: number): boolean {
  if (left < 0 || top < 0 || left + width > floor.width || top + height > floor.depth) return false
  const box = { x: left, y: top, width, height }
  return Object.entries(floor.rooms).every(([other, at]) => other === id || !sizes[other] || !intersects(box, { x: at.left, y: at.top, ...sizes[other] }))
}

/**
 * The drafts with one room changed by `change`, starting from how that room looks now.
 * Editing anything fixes the building as drawn, so nothing else moves meanwhile; a change
 * that would push the room into another one, or out of the building, is left undone.
 */
export function editRoom(shown: OfficeView, drafts: Drafts, roomId: string, change: (a: Arrangement) => Arrangement): Drafts {
  const arrangement = currentArrangement(shown, drafts, roomId)
  if (!arrangement) return drafts
  const floor = currentFloor(shown, drafts)
  const changed = change(arrangement)
  const at = floor.rooms[roomId]
  if (at && !fits(floor, roomSizes(shown, drafts), roomId, at.left, at.top, changed.width, changed.depth + WALL)) return drafts
  return { ...drafts, auto: false, floor, rooms: { ...drafts.rooms, [roomId]: changed } }
}

/** The drafts with the building changed by `change`. */
export function editFloor(shown: OfficeView, drafts: Drafts, change: (f: FloorArrangement, sizes: Sizes) => FloorArrangement): Drafts {
  return { ...drafts, auto: false, floor: change(currentFloor(shown, drafts), roomSizes(shown, drafts)) }
}

export const placeIn = (shown: OfficeView, drafts: Drafts, roomId: string, target: Target, x: number, y: number) =>
  roomId === BUILDING && target.kind === 'piece'
    ? editFloor(shown, drafts, (f) => placeCommon(f, target.index, x, y))
    : editRoom(shown, drafts, roomId, (a) => place(a, target, x, y))

export const layoutRequest = (arrangement: Arrangement): RoomLayoutRequest => ({
  width: arrangement.width,
  depth: arrangement.depth,
  desks: Object.entries(arrangement.desks).map(([deskId, spot]) => ({ deskId, ...spot })),
  furniture: arrangement.furniture,
  door: arrangement.door,
  windows: arrangement.windows,
})

/** The whole building as shown, as the API takes it. */
export function planRequest(shown: OfficeView): FloorPlanRequest {
  const layout = layoutOffice(shown)
  const floor = floorArrangementOf(layout)
  return {
    width: floor.width,
    depth: floor.depth,
    furniture: floor.furniture,
    rooms: layout.rooms.map((box) => ({ roomId: box.id, ...floor.rooms[box.id], layout: layoutRequest(arrangementOf(box)) })),
  }
}

const sizeOf = (arrangement: Arrangement) => ({ width: arrangement.width * TILE, height: (arrangement.depth + WALL) * TILE })

/** The box a target takes in its room. */
export function boundsOf(arrangement: Arrangement, target: Target): Rect | undefined {
  const room = sizeOf(arrangement)
  switch (target.kind) {
    case 'desk': {
      const spot = arrangement.desks[target.id]
      return spot && { ...spot, width: STATION.width, height: STATION.height }
    }
    case 'piece': {
      const piece = arrangement.furniture[target.index]
      if (!piece || !isProp(piece.name)) return undefined
      const { sprite } = FURNITURE[piece.name]
      return { x: piece.x, y: piece.y, width: sprite.width, height: sprite.height }
    }
    case 'window': {
      const x = arrangement.windows[target.index]
      return x === undefined ? undefined : { x, y: WINDOW.y, width: WINDOW.width, height: WINDOW.height }
    }
    case 'door': {
      const { wall, offset } = arrangement.door
      const length = DOOR * TILE
      if (wall === 'Front') return { x: offset * TILE, y: room.height - 6, width: length, height: 10 }
      if (wall === 'Back') return { x: offset * TILE, y: 0, width: length, height: WALL_HEIGHT }
      const y = (WALL + offset) * TILE
      return wall === 'Left' ? { x: -3, y, width: 9, height: length } : { x: room.width - 6, y, width: 9, height: length }
    }
  }
}

/**
 * The nearest place a target may stand: inside the room, desks with their heads against
 * the wall at most, furniture on the floor, rugs flat on it, posters and windows on the
 * wall, the door along one of the walls.
 */
function allowed(arrangement: Arrangement, target: Target, x: number, y: number): { x: number; y: number } {
  const room = sizeOf(arrangement)
  const box = boundsOf(arrangement, target)
  if (!box) return { x, y }
  const right = Math.max(0, room.width - box.width)
  const bottom = Math.max(0, room.height - box.height)
  if (target.kind === 'window') return { x: clamp(x, 0, room.width - WINDOW.width), y: WINDOW.y }
  if (target.kind === 'desk') return { x: clamp(x, 0, right), y: clamp(y, Math.min(bottom, WALL_HEIGHT - 12), bottom) }
  if (target.kind !== 'piece') return { x, y }
  const layer = FURNITURE[arrangement.furniture[target.index].name as PropName].layer
  const top = layer === 'wall' ? 0 : layer === 'floor' ? WALL_HEIGHT : Math.max(0, WALL_HEIGHT + 2 - box.height)
  const lowest = layer === 'wall' ? Math.max(0, WALL_HEIGHT - box.height) : bottom
  return { x: clamp(x, 0, right), y: clamp(y, Math.min(top, lowest), lowest) }
}

/** Moves a target to the nearest whole pixel it may stand on; a door goes to the wall nearest the point. */
export function place(arrangement: Arrangement, target: Target, x: number, y: number): Arrangement {
  if (target.kind === 'door') return { ...arrangement, door: doorAt(arrangement, { x, y }) }
  const spot = allowed(arrangement, target, Math.round(x), Math.round(y))
  if (target.kind === 'desk') return { ...arrangement, desks: { ...arrangement.desks, [target.id]: spot } }
  if (target.kind === 'window') return { ...arrangement, windows: arrangement.windows.map((w, i) => (i === target.index ? spot.x : w)) }
  return { ...arrangement, furniture: arrangement.furniture.map((p, i) => (i === target.index ? { ...p, ...spot } : p)) }
}

/** The door on the wall nearest a point of the room, centred on it as far as the wall allows. */
export function doorAt(arrangement: Arrangement, point: { x: number; y: number }): DoorPlacement {
  const room = sizeOf(arrangement)
  const walls = [
    { wall: 'Front', distance: Math.abs(point.y - room.height), along: point.x, length: arrangement.width },
    { wall: 'Back', distance: Math.abs(point.y - WALL_HEIGHT / 2), along: point.x, length: arrangement.width },
    { wall: 'Left', distance: Math.abs(point.x), along: point.y - WALL_HEIGHT, length: arrangement.depth },
    { wall: 'Right', distance: Math.abs(point.x - room.width), along: point.y - WALL_HEIGHT, length: arrangement.depth },
  ] as const
  const nearest = walls.reduce<(typeof walls)[number]>((a, b) => (b.distance < a.distance ? b : a), walls[0])
  return { wall: nearest.wall, offset: clamp(Math.round(nearest.along / TILE - DOOR / 2), 0, Math.max(0, nearest.length - DOOR)) }
}

/** Every target of a room, from the one drawn in front to the one at the back. */
function frontToBack(arrangement: Arrangement): Target[] {
  const targets: { target: Target; order: number }[] = [
    ...Object.keys(arrangement.desks).map((id) => ({ target: { kind: 'desk', id } as Target, order: 4 })),
    ...arrangement.furniture.map((p, index) => {
      const layer = isProp(p.name) ? FURNITURE[p.name].layer : undefined
      return { target: { kind: 'piece', index } as Target, order: layer === 'floor' ? 0 : layer === 'wall' ? 1 : 4 }
    }),
    ...arrangement.windows.map((_, index) => ({ target: { kind: 'window', index } as Target, order: 3 })),
    { target: { kind: 'door' } as Target, order: 2 },
  ]
  const base = (t: Target) => {
    const box = boundsOf(arrangement, t)
    return box ? box.y + box.height : 0
  }
  return targets.sort((a, b) => b.order - a.order || base(b.target) - base(a.target)).map((t) => t.target)
}

/** Whether a sprite has a pixel at (x, y) of its first frame. */
function solid(name: PropName, x: number, y: number): boolean {
  const row = FURNITURE[name].sprite.frames[0][y]
  return row !== undefined && row[x] !== undefined && row[x] !== '.'
}

/** What is drawn at a point of the room, in pixels from its corner: the front-most thing there. */
export function targetAt(arrangement: Arrangement, point: { x: number; y: number }): Target | undefined {
  return frontToBack(arrangement).find((target) => {
    const box = boundsOf(arrangement, target)
    if (!box) return false
    const x = Math.floor(point.x - box.x)
    const y = Math.floor(point.y - box.y)
    if (x < 0 || y < 0 || x >= box.width || y >= box.height) return false
    // The top of a station is room for its signal: the desk starts at the head.
    if (target.kind === 'desk') return y >= 8
    if (target.kind !== 'piece') return true
    return solid(arrangement.furniture[target.index].name as PropName, x, y)
  })
}

/**
 * Adds a piece where it covers nothing, as near the middle of the floor as it can; in the
 * middle when the room is full. It is the last piece, so its index is the length before.
 */
export function addPiece(arrangement: Arrangement, name: PropName): Arrangement {
  const { sprite } = FURNITURE[name]
  const room = sizeOf(arrangement)
  const added = { ...arrangement, furniture: [...arrangement.furniture, { name, x: 0, y: 0 }] }
  const target: Target = { kind: 'piece', index: added.furniture.length - 1 }
  const middle = place(added, target, (room.width - sprite.width) / 2, WALL_HEIGHT + (room.height - WALL_HEIGHT - sprite.height) / 2)
  const others = [
    ...Object.keys(arrangement.desks).map((id) => boundsOf(arrangement, { kind: 'desk', id })!),
    ...arrangement.furniture.map((_, index) => boundsOf(arrangement, { kind: 'piece', index })).filter((b) => b !== undefined),
  ]
  const centre = middle.furniture[target.index]
  let best: { arrangement: Arrangement; distance: number } | undefined
  for (let y = 0; y < room.height; y += 4)
    for (let x = 0; x < room.width; x += 4) {
      const candidate = place(added, target, x, y)
      const box = boundsOf(candidate, target)!
      if (others.some((o) => intersects(box, o))) continue
      const distance = Math.hypot(box.x - centre.x, box.y - centre.y)
      if (!best || distance < best.distance) best = { arrangement: candidate, distance }
    }
  return best?.arrangement ?? middle
}

export const removePiece = (arrangement: Arrangement, index: number): Arrangement => ({
  ...arrangement,
  furniture: arrangement.furniture.filter((_, i) => i !== index),
})

/** Adds a window in the back wall where there is none yet and no door, near the middle; at the left end when the wall is full. */
export function addWindow(arrangement: Arrangement): Arrangement {
  const room = sizeOf(arrangement)
  const taken = [
    ...arrangement.windows.map((x) => ({ x, width: WINDOW.width })),
    ...(arrangement.door.wall === 'Back' ? [{ x: arrangement.door.offset * TILE, width: DOOR * TILE }] : []),
  ]
  const centre = (room.width - WINDOW.width) / 2
  let best: number | undefined
  for (let x = 4; x + WINDOW.width <= room.width - 4; x += 4) {
    if (taken.some((t) => x < t.x + t.width + 4 && t.x < x + WINDOW.width + 4)) continue
    if (best === undefined || Math.abs(x - centre) < Math.abs(best - centre)) best = x
  }
  return { ...arrangement, windows: [...arrangement.windows, best ?? 0] }
}

export const removeWindow = (arrangement: Arrangement, index: number): Arrangement => ({
  ...arrangement,
  windows: arrangement.windows.filter((_, i) => i !== index),
})

/** Desks back in rows of four, as a new room would have them. */
export function tidyDesks(arrangement: Arrangement): Arrangement {
  let tidy: Arrangement = { ...arrangement, desks: {} }
  Object.keys(arrangement.desks).forEach((id, slot) => {
    const spot = autoSlot(slot)
    tidy = place({ ...tidy, desks: { ...tidy.desks, [id]: spot } }, { kind: 'desk', id }, spot.x, spot.y)
  })
  return tidy
}

/** A new size within the limits; whatever would fall outside moves back in. */
export function resize(arrangement: Arrangement, width: number, depth: number): Arrangement {
  let resized: Arrangement = {
    ...arrangement,
    width: clamp(width, LIMITS.width[0], LIMITS.width[1]),
    depth: clamp(depth, LIMITS.depth[0], LIMITS.depth[1]),
  }
  for (const id of Object.keys(resized.desks)) resized = place(resized, { kind: 'desk', id }, resized.desks[id].x, resized.desks[id].y)
  resized.furniture.forEach((p, index) => (resized = place(resized, { kind: 'piece', index }, p.x, p.y)))
  resized.windows.forEach((x, index) => (resized = place(resized, { kind: 'window', index }, x, WINDOW.y)))
  const length = resized.door.wall === 'Left' || resized.door.wall === 'Right' ? resized.depth : resized.width
  return { ...resized, door: { ...resized.door, offset: clamp(resized.door.offset, 0, length - DOOR) } }
}

/** The box of a piece of the common floor, in pixels from the building's corner. */
export function commonBounds(floor: FloorArrangement, index: number): Rect | undefined {
  const piece = floor.furniture[index]
  if (!piece || !isProp(piece.name)) return undefined
  const { sprite } = FURNITURE[piece.name]
  return { x: piece.x, y: piece.y, width: sprite.width, height: sprite.height }
}

/** Moves a piece of the common floor, inside the building. */
export function placeCommon(floor: FloorArrangement, index: number, x: number, y: number): FloorArrangement {
  const box = commonBounds(floor, index)
  if (!box) return floor
  const spot = { x: clamp(Math.round(x), 0, floor.width * TILE - box.width), y: clamp(Math.round(y), 0, floor.depth * TILE - box.height) }
  return { ...floor, furniture: floor.furniture.map((p, i) => (i === index ? { ...p, ...spot } : p)) }
}

/** The piece of the common floor drawn at a point, front-most first. */
export function commonAt(floor: FloorArrangement, point: { x: number; y: number }): number | undefined {
  const baseOf = (i: number) => {
    const box = commonBounds(floor, i)
    return box ? box.y + box.height : 0
  }
  return floor.furniture
    .map((_, i) => i)
    .sort((a, b) => baseOf(b) - baseOf(a))
    .find((index) => {
      const box = commonBounds(floor, index)
      if (!box) return false
      const x = Math.floor(point.x - box.x)
      const y = Math.floor(point.y - box.y)
      return x >= 0 && y >= 0 && x < box.width && y < box.height && solid(floor.furniture[index].name as PropName, x, y)
    })
}

/** Adds a piece to the common floor, on the free spot nearest the middle, outside the rooms. */
export function addCommon(floor: FloorArrangement, sizes: Sizes, name: PropName): FloorArrangement {
  const { sprite } = FURNITURE[name]
  const rooms = Object.entries(floor.rooms)
    .filter(([id]) => sizes[id])
    .map(([id, at]) => ({ x: at.left * TILE, y: at.top * TILE, width: sizes[id].width * TILE, height: sizes[id].height * TILE }))
  const pieces = floor.furniture.map((_, i) => commonBounds(floor, i)).filter((b) => b !== undefined)
  const centre = { x: (floor.width * TILE - sprite.width) / 2, y: (floor.depth * TILE - sprite.height) / 2 }
  let best: { x: number; y: number; distance: number } | undefined
  for (let y = 0; y + sprite.height <= floor.depth * TILE; y += 4)
    for (let x = 0; x + sprite.width <= floor.width * TILE; x += 4) {
      const box = { x, y, width: sprite.width, height: sprite.height }
      if (rooms.some((r) => intersects(box, r)) || pieces.some((p) => intersects(box, p))) continue
      const distance = Math.hypot(x - centre.x, y - centre.y)
      if (!best || distance < best.distance) best = { x, y, distance }
    }
  return { ...floor, furniture: [...floor.furniture, { name, x: best?.x ?? 0, y: best?.y ?? 0 }] }
}

export const removeCommon = (floor: FloorArrangement, index: number): FloorArrangement => ({
  ...floor,
  furniture: floor.furniture.filter((_, i) => i !== index),
})

/** Moves a room to a place, in tiles, if it fits there; otherwise it stays. */
export function moveRoom(floor: FloorArrangement, sizes: Sizes, id: string, left: number, top: number): FloorArrangement {
  const size = sizes[id]
  if (!size) return floor
  const at = { left: clamp(left, 0, floor.width - size.width), top: clamp(top, 0, floor.depth - size.height) }
  return fits(floor, sizes, id, at.left, at.top, size.width, size.height) ? { ...floor, rooms: { ...floor.rooms, [id]: at } } : floor
}

/** A new size for the building, never smaller than what its rooms need; common furniture moves back in. */
export function resizeFloor(floor: FloorArrangement, sizes: Sizes, width: number, depth: number): FloorArrangement {
  const placed = Object.entries(floor.rooms).filter(([id]) => sizes[id])
  const needs = {
    width: Math.max(LIMITS.floorWidth[0], ...placed.map(([id, at]) => at.left + sizes[id].width)),
    depth: Math.max(LIMITS.floorDepth[0], ...placed.map(([id, at]) => at.top + sizes[id].height)),
  }
  let resized: FloorArrangement = {
    ...floor,
    width: clamp(width, needs.width, LIMITS.floorWidth[1]),
    depth: clamp(depth, needs.depth, LIMITS.floorDepth[1]),
  }
  resized.furniture.forEach((p, i) => (resized = placeCommon(resized, i, p.x, p.y)))
  return resized
}

export const sameTarget = (a: Target | null | undefined, b: Target | null | undefined) => !!a && !!b && JSON.stringify(a) === JSON.stringify(b)

export type Edge = 'right' | 'bottom' | 'corner'

/** Handles on the right wall, the front edge and the corner of a box in tiles, in world pixels, just outside it. */
export function handlesOf(box: { x: number; y: number; width: number; height: number }): { edge: Edge; x: number; y: number }[] {
  const right = (box.x + box.width) * TILE + 2
  const bottom = (box.y + box.height) * TILE + 2
  return [
    // Three quarters along, so they never sit on a door in the middle of a wall.
    { edge: 'right', x: right, y: (box.y + (box.height * 3) / 4) * TILE },
    { edge: 'bottom', x: (box.x + (box.width * 3) / 4) * TILE, y: bottom },
    { edge: 'corner', x: right, y: bottom },
  ]
}
