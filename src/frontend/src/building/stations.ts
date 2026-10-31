import type { DeskPlace, Errand, StationSpot } from '../activity/walks'
import { FURNITURE } from '../art/fanPack'
import { TILE, WALL_HEIGHT } from '../art/roomScene'
import type { PropName } from '../art/sheets/props'
import { STATION } from '../art/station'
import { WALKER } from '../art/walker'
import type { BuildingLayout, RoomBox } from './layout'

/** What each piece of furniture is for: agents walk to it for that, and only for that. */
export const ERRANDS: Partial<Record<PropName, Errand>> = {
  bookshelf: 'documents',
  filingCabinet: 'documents',
  printer: 'documents',
  serverRack: 'commands',
  whiteboard: 'plans',
  coffeeMachine: 'break',
  waterCooler: 'break',
  vendingMachine: 'break',
  sideTable: 'break',
  arcadeCabinet: 'break',
}

/** What agents of the same room come to a piece of furniture for, in words. */
export const ERRAND_WORDS: Record<Errand, string> = {
  documents: 'Agents of this room walk here now and then to read.',
  commands: 'Agents of this room walk here now and then to run commands and tests.',
  plans: 'Agents of this room walk here now and then to plan.',
  break: 'Agents of this room take their breaks here.',
}

/** Where a seated agent's standing frame is, from its desk's corner: the same head and body as at the desk. */
const SEAT = { x: 8, y: STATION.deskY - 14 }

/** A standing agent's frame keeps inside its room: head over the back wall at most, feet on the floor. */
function inside(room: RoomBox, x: number, y: number) {
  const [left, top] = [room.x * TILE, room.y * TILE]
  return {
    x: Math.max(left, Math.min(x, left + room.width * TILE - WALKER.width)),
    y: Math.max(top + WALL_HEIGHT - 24, Math.min(y, top + room.height * TILE - WALKER.height - 2)),
  }
}

/**
 * Where agents stand to use the furniture of a room: in front of it facing it for work,
 * beside it facing the room on a break, a little apart by desk so two at the same place
 * stand side by side. Furniture on walls and floors serves nothing.
 */
function stationsOf(room: RoomBox): { name: PropName; spot: StationSpot }[] {
  const [left, top] = [room.x * TILE, room.y * TILE]
  return room.props.flatMap((p): { name: PropName; spot: StationSpot }[] => {
    const errand = ERRANDS[p.name]
    const furniture = FURNITURE[p.name]
    if (!errand || furniture.layer) return []
    const { width, height } = furniture.sprite
    // Kept inside the room the way the furniture itself is drawn.
    const px = Math.max(0, Math.min(p.x, room.width * TILE - width))
    const py = Math.max(0, Math.min(p.y, room.height * TILE - height))
    const base = top + py + height - WALKER.height
    if (errand !== 'break') return [{ name: p.name, spot: { ...inside(room, left + px + width / 2 - WALKER.width / 2, base + 6), errand, facing: 'back' } }]
    // On a break it stands beside the machine, facing the room, so the machine still shows.
    const beside = px - WALKER.width + 6 >= 0 ? left + px - WALKER.width + 6 : left + px + width - 6
    return [{ name: p.name, spot: { ...inside(room, beside, base + 2), errand, facing: 'front' } }]
  })
}

/** Every desk of the building with its seat, its ways out and the stations of its room. */
export function placesOf(layout: BuildingLayout): DeskPlace[] {
  return layout.rooms.flatMap((room) => {
    const stations = stationsOf(room)
    return room.desks.map((d, i) => {
      const seat = { x: d.x + SEAT.x, y: d.y + SEAT.y }
      const exits = [inside(room, d.x - WALKER.width + 2, seat.y), inside(room, d.x + STATION.width - 2, seat.y)] as const
      // Agents at the same station stand a little apart.
      const shift = ((i % 3) - 1) * 8
      return { deskId: d.id, roomId: room.id, seat, exits, stations: stations.map((s) => ({ ...s.spot, x: s.spot.x + shift })) }
    })
  })
}
