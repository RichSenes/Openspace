import { TILE, type BuildingLayout } from './layout'

/** A rectangle of wall, in world pixels. */
export interface Strip {
  x: number
  y: number
  width: number
  height: number
}

/** Walls seen from above are a thin cap: this thick, centred on the line between two spaces. */
export const WALL_CAP = 4
export const OUTER_CAP = 6
/** How much of a front wall shows below its cap, on the side facing the viewer. */
export const WALL_FACE = 7

/** What to draw for the walls of a building: caps, the faces of front walls and door jambs. */
export interface WallPlan {
  caps: Strip[]
  faces: Strip[]
  jambs: Strip[]
}

interface Segment {
  horizontal: boolean
  /** The y of a horizontal line, the x of a vertical one. */
  at: number
  from: number
  to: number
  cap: number
  face: boolean
}

/**
 * The walls of a building: around it, around every room, and open wherever a door is,
 * even where two rooms share a wall or a room's wall is the building's own. Rooms side by
 * side draw the same line twice, which is harmless.
 */
export function wallPlan(layout: BuildingLayout): WallPlan {
  const { floor } = layout
  const [west, north, east, south] = [floor.x * TILE, floor.y * TILE, (floor.x + floor.width) * TILE, (floor.y + floor.height) * TILE]
  const segments: Segment[] = [
    { horizontal: true, at: north, from: west, to: east, cap: OUTER_CAP, face: false },
    { horizontal: true, at: south, from: west, to: east, cap: OUTER_CAP, face: false },
    { horizontal: false, at: west, from: north, to: south, cap: OUTER_CAP, face: false },
    { horizontal: false, at: east, from: north, to: south, cap: OUTER_CAP, face: false },
  ]
  for (const room of layout.rooms) {
    const [left, top, right, bottom] = [room.x * TILE, room.y * TILE, (room.x + room.width) * TILE, (room.y + room.height) * TILE]
    segments.push(
      { horizontal: true, at: top, from: left, to: right, cap: WALL_CAP, face: false },
      // The front wall is seen from in front: a few pixels of it show below the cap.
      { horizontal: true, at: bottom, from: left, to: right, cap: WALL_CAP, face: bottom !== south },
      { horizontal: false, at: left, from: top, to: bottom, cap: WALL_CAP, face: false },
      { horizontal: false, at: right, from: top, to: bottom, cap: WALL_CAP, face: false },
    )
  }

  const gaps = layout.doors.map((d) => ({ horizontal: d.wall === 'Front' || d.wall === 'Back', at: d.wall === 'Front' || d.wall === 'Back' ? d.y : d.x, from: d.wall === 'Front' || d.wall === 'Back' ? d.x : d.y, to: (d.wall === 'Front' || d.wall === 'Back' ? d.x : d.y) + d.length }))
  const plan: WallPlan = { caps: [], faces: [], jambs: [] }
  for (const segment of segments) {
    let pieces = [{ from: segment.from, to: segment.to }]
    for (const gap of gaps) {
      if (gap.horizontal !== segment.horizontal || gap.at !== segment.at) continue
      pieces = pieces.flatMap((p) =>
        gap.to <= p.from || gap.from >= p.to ? [p] : [{ from: p.from, to: gap.from }, { from: gap.to, to: p.to }].filter((q) => q.to > q.from),
      )
    }
    const half = segment.cap / 2
    for (const p of pieces) {
      // Pieces that end at a door stop at its edge; the others reach over the corners.
      const start = gaps.some((g) => g.horizontal === segment.horizontal && g.at === segment.at && g.to === p.from) ? p.from : p.from - half
      const end = gaps.some((g) => g.horizontal === segment.horizontal && g.at === segment.at && g.from === p.to) ? p.to : p.to + half
      plan.caps.push(
        segment.horizontal
          ? { x: start, y: segment.at - half, width: end - start, height: segment.cap }
          : { x: segment.at - half, y: start, width: segment.cap, height: end - start },
      )
      if (segment.face) plan.faces.push({ x: start, y: segment.at + half, width: end - start, height: WALL_FACE })
    }
  }
  for (const gap of gaps)
    for (const end of [gap.from, gap.to])
      plan.jambs.push(
        gap.horizontal
          ? { x: end - WALL_CAP / 2 - 1, y: gap.at - 4, width: WALL_CAP + 2, height: 8 }
          : { x: gap.at - 4, y: end - WALL_CAP / 2 - 1, width: 8, height: WALL_CAP + 2 },
      )
  return plan
}
