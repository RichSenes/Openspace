import { inBuilding, TILE, type BuildingLayout } from './layout'

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
 * Runs of tiles along a line where `edge(i)` holds, as [from, to) pairs: the walls of a
 * shaped building follow its tiles, and a front wall shows its face only where it does.
 */
function runs(from: number, to: number, edge: (i: number) => boolean): [number, number][] {
  const found: [number, number][] = []
  for (let i = from; i < to; i++) {
    if (!edge(i)) continue
    const last = found[found.length - 1]
    if (last && last[1] === i) last[1] = i + 1
    else found.push([i, i + 1])
  }
  return found
}

/**
 * The walls of a building: along its outline, around every room, and open wherever a door
 * is, even where two rooms share a wall or a room's wall is the building's own. Rooms side
 * by side draw the same line twice, which is harmless.
 */
export function wallPlan(layout: BuildingLayout): WallPlan {
  const { floor } = layout
  const inside = (x: number, y: number) => inBuilding(layout, x, y)
  const segments: Segment[] = []
  // The outline: wherever a tile of the building meets one that is not.
  for (let y = floor.y; y <= floor.y + floor.height; y++)
    for (const [a, b] of runs(floor.x, floor.x + floor.width, (x) => inside(x, y - 1) !== inside(x, y)))
      segments.push({ horizontal: true, at: y * TILE, from: a * TILE, to: b * TILE, cap: OUTER_CAP, face: false })
  for (let x = floor.x; x <= floor.x + floor.width; x++)
    for (const [a, b] of runs(floor.y, floor.y + floor.height, (y) => inside(x - 1, y) !== inside(x, y)))
      segments.push({ horizontal: false, at: x * TILE, from: a * TILE, to: b * TILE, cap: OUTER_CAP, face: false })

  for (const room of layout.rooms) {
    const [left, top, right, bottom] = [room.x * TILE, room.y * TILE, (room.x + room.width) * TILE, (room.y + room.height) * TILE]
    segments.push(
      { horizontal: true, at: top, from: left, to: right, cap: WALL_CAP, face: false },
      { horizontal: false, at: left, from: top, to: bottom, cap: WALL_CAP, face: false },
      { horizontal: false, at: right, from: top, to: bottom, cap: WALL_CAP, face: false },
    )
    // The front wall is seen from in front: a few pixels of it show below the cap, where
    // there is floor in front of it and not the outside.
    const below = room.y + room.height
    for (const face of [true, false])
      for (const [a, b] of runs(room.x, room.x + room.width, (x) => inside(x, below) === face))
        segments.push({ horizontal: true, at: bottom, from: a * TILE, to: b * TILE, cap: WALL_CAP, face })
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
