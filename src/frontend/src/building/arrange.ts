import type { FurniturePiece, OfficeView, RoomLayoutRequest } from '../api/types'
import { FURNITURE } from '../art/fanPack'
import { WALL_HEIGHT } from '../art/roomScene'
import { PROPS, type PropName } from '../art/sheets/props'
import { STATION } from '../art/station'
import { isProp, layoutOffice, TILE, WALL, type RoomBox } from './layout'

/** A room being arranged: its size in tiles, its desks and furniture in pixels from its corner. */
export interface Arrangement {
  width: number
  depth: number
  desks: Record<string, { x: number; y: number }>
  furniture: FurniturePiece[]
}

/** Arrangements not saved yet, by room; 'auto' gives a room back to the automatic layout. */
export type Drafts = Record<string, Arrangement | 'auto'>

/** What can be picked up in a room. */
export type Target = { kind: 'desk'; id: string } | { kind: 'piece'; index: number }

export interface Rect {
  x: number
  y: number
  width: number
  height: number
}

/** Room sizes the editor offers, in tiles; the API allows a little more. */
export const LIMITS = { width: [4, 40], depth: [3, 30] } as const

/** Every piece of furniture, in the order of the sheet. */
export const FURNITURE_NAMES = Object.keys(PROPS) as PropName[]

/** "sideTable" reads "Side table". */
export const furnitureLabel = (name: string) => {
  const words = name.replace(/([A-Z])/g, ' $1').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/** The arrangement a room has now, as drawn: the starting point of an edit. */
export function arrangementOf(box: RoomBox): Arrangement {
  const left = box.x * TILE
  const top = box.y * TILE
  return {
    width: box.width,
    depth: box.height - WALL,
    desks: Object.fromEntries(box.desks.map((d) => [d.id, { x: d.x - left, y: d.y - top }])),
    furniture: box.props.map((p) => ({ ...p })),
  }
}

/** The office as it will look once the drafts are saved. */
export function withDrafts(office: OfficeView, drafts: Drafts): OfficeView {
  return {
    ...office,
    rooms: office.rooms.map((room) => {
      const draft = drafts[room.id]
      if (!draft) return room
      if (draft === 'auto') return { ...room, width: null, depth: null, furniture: [], desks: room.desks.map((d) => ({ ...d, spot: null })) }
      return {
        ...room,
        width: draft.width,
        depth: draft.depth,
        furniture: draft.furniture,
        desks: room.desks.map((d) => ({ ...d, spot: draft.desks[d.id] ?? null })),
      }
    }),
  }
}

export const layoutRequest = (arrangement: Arrangement): RoomLayoutRequest => ({
  width: arrangement.width,
  depth: arrangement.depth,
  desks: Object.entries(arrangement.desks).map(([deskId, spot]) => ({ deskId, ...spot })),
  furniture: arrangement.furniture,
})

const clamp = (value: number, low: number, high: number) => Math.max(low, Math.min(high, value))

const sizeOf = (arrangement: Arrangement) => ({ width: arrangement.width * TILE, height: (arrangement.depth + WALL) * TILE })

/** The box a target takes in its room. */
export function boundsOf(arrangement: Arrangement, target: Target): Rect | undefined {
  if (target.kind === 'desk') {
    const spot = arrangement.desks[target.id]
    return spot && { ...spot, width: STATION.width, height: STATION.height }
  }
  const piece = arrangement.furniture[target.index]
  if (!piece || !isProp(piece.name)) return undefined
  const { sprite } = FURNITURE[piece.name]
  return { x: piece.x, y: piece.y, width: sprite.width, height: sprite.height }
}

/**
 * The nearest place a target may stand: inside the room, desks with their heads against
 * the wall at most, furniture on the floor, rugs flat on it and posters on the wall.
 */
function allowed(arrangement: Arrangement, target: Target, x: number, y: number): { x: number; y: number } {
  const room = sizeOf(arrangement)
  const box = boundsOf(arrangement, target)
  if (!box) return { x, y }
  const right = Math.max(0, room.width - box.width)
  const bottom = Math.max(0, room.height - box.height)
  if (target.kind === 'desk') return { x: clamp(x, 0, right), y: clamp(y, Math.min(bottom, WALL_HEIGHT - 12), bottom) }
  const layer = FURNITURE[arrangement.furniture[target.index].name as PropName].layer
  const top = layer === 'wall' ? 0 : layer === 'floor' ? WALL_HEIGHT : Math.max(0, WALL_HEIGHT + 2 - box.height)
  const lowest = layer === 'wall' ? Math.max(0, WALL_HEIGHT - box.height) : bottom
  return { x: clamp(x, 0, right), y: clamp(y, Math.min(top, lowest), lowest) }
}

/** Moves a target to the nearest whole pixel it may stand on. */
export function place(arrangement: Arrangement, target: Target, x: number, y: number): Arrangement {
  const spot = allowed(arrangement, target, Math.round(x), Math.round(y))
  if (target.kind === 'desk') return { ...arrangement, desks: { ...arrangement.desks, [target.id]: spot } }
  return { ...arrangement, furniture: arrangement.furniture.map((p, i) => (i === target.index ? { ...p, ...spot } : p)) }
}

/** Every target of a room, from the one drawn in front to the one at the back. */
function frontToBack(arrangement: Arrangement): Target[] {
  const targets: { target: Target; order: number }[] = [
    ...Object.keys(arrangement.desks).map((id) => ({ target: { kind: 'desk', id } as Target, order: 2 })),
    ...arrangement.furniture.map((p, index) => {
      const layer = isProp(p.name) ? FURNITURE[p.name].layer : undefined
      return { target: { kind: 'piece', index } as Target, order: layer === 'floor' ? 0 : layer === 'wall' ? 1 : 2 }
    }),
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
    return solid(arrangement.furniture[target.index].name as PropName, x, y)
  })
}

const intersects = (a: Rect, b: Rect) => a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height

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

/** A new size within the limits; whatever would fall outside moves back in. */
export function resize(arrangement: Arrangement, width: number, depth: number): Arrangement {
  let resized: Arrangement = {
    ...arrangement,
    width: clamp(width, LIMITS.width[0], LIMITS.width[1]),
    depth: clamp(depth, LIMITS.depth[0], LIMITS.depth[1]),
  }
  for (const id of Object.keys(resized.desks)) resized = place(resized, { kind: 'desk', id }, resized.desks[id].x, resized.desks[id].y)
  resized.furniture.forEach((p, index) => (resized = place(resized, { kind: 'piece', index }, p.x, p.y)))
  return resized
}

export const sameTarget = (a: Target | null | undefined, b: Target | null | undefined) =>
  !!a && !!b && (a.kind === 'desk' ? b.kind === 'desk' && a.id === b.id : b.kind === 'piece' && a.index === b.index)

/** The arrangement a room has in the office as shown: its draft, or how it is drawn now. */
export function currentArrangement(shown: OfficeView, drafts: Drafts, roomId: string): Arrangement | undefined {
  const draft = drafts[roomId]
  if (draft && draft !== 'auto') return draft
  const box = layoutOffice(shown, Number.MAX_SAFE_INTEGER).rooms.find((r) => r.id === roomId)
  return box && arrangementOf(box)
}

/** The drafts with one room changed by `change`, starting from how that room looks now. */
export function editRoom(shown: OfficeView, drafts: Drafts, roomId: string, change: (a: Arrangement) => Arrangement): Drafts {
  const arrangement = currentArrangement(shown, drafts, roomId)
  return arrangement ? { ...drafts, [roomId]: change(arrangement) } : drafts
}

export const placeIn = (shown: OfficeView, drafts: Drafts, roomId: string, target: Target, x: number, y: number) =>
  editRoom(shown, drafts, roomId, (a) => place(a, target, x, y))

export type Edge = 'right' | 'bottom' | 'corner'

/** The handles on the edges of a room being arranged, in world pixels, just outside its walls. */
export function handlesOf(room: RoomBox): { edge: Edge; x: number; y: number }[] {
  const right = (room.x + room.width) * TILE + 2
  const bottom = (room.y + room.height) * TILE + 2
  return [
    { edge: 'right', x: right, y: (room.y + room.height / 2) * TILE },
    { edge: 'bottom', x: (room.x + room.width / 2) * TILE, y: bottom },
    { edge: 'corner', x: right, y: bottom },
  ]
}
