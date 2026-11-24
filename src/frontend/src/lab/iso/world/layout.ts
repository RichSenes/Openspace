/**
 * The lab's floor plan, made from the offices someone sets up: nothing is drawn by hand. It is
 * one big open space. Shiori's area is the wide open middle; every office is an area around it,
 * sized by how many agents work there and shaped by its name (the same setup always gives the
 * same lab), set off by low glass partitions with an opening towards her; a lounge is added for
 * everybody. Areas sit round her on four sides, the biggest at the back; the free floor between
 * them is the walkway. The building's outline is the union of it all, so its shape follows the
 * setup. A grid of tiles: each tile is outside (-1), free floor, Shiori's area, or an area.
 */

export type Flavor = 'code' | 'research' | 'creative' | 'data'

export interface OfficeSpec {
  name: string
  accent: string
  agents: number
  flavor: Flavor
}

export type SpaceKind = 'floor' | 'hall' | 'office' | 'lounge' | 'games'

export interface Space {
  id: number
  kind: SpaceKind
  name: string
  accent: string
  flavor: Flavor
  agents: number
  /** Tiles, half-open: a0 ≤ a < a1, b0 ≤ b < b1. */
  a0: number
  b0: number
  a1: number
  b1: number
  side: Side
}

export type Side = '-a' | '+a' | '-b' | '+b'

export interface Plan {
  /** Size of the grid in tiles, everything around included. */
  w: number
  h: number
  spaces: Space[]
  /** For each tile, the id of its space, or -1 outside. */
  cells: Int16Array
  /** Edges where a partition opens. Keyed by edgeKey. */
  doors: Set<string>
  hall: Space
  /** The building's extent in tiles: a0, b0, a1, b1. */
  box: [number, number, number, number]
}

/** Shiori's area: wide, the heart of the lab. */
export const HALL = 17
/** The walkway between her area and the offices. */
const AISLE = 2
const LOUNGE: OfficeSpec = { name: 'Lounge', accent: '#c48bff', agents: 2, flavor: 'data' }
/** The break room: games for several at once. Both common rooms grow with the number of agents. */
const GAMES: OfficeSpec = { name: 'Break room', accent: '#ffb347', agents: 2, flavor: 'creative' }

/** A repeatable number in [0, 1) from a name. */
export function hashOf(text: string, salt = 0): number {
  let h = 2166136261 ^ salt
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619)
  h ^= h >>> 13
  h = Math.imul(h, 1274126177)
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296
}

/** An area's size in tiles: more agents, more room; its proportions come from its name. */
export function roomSize(spec: OfficeSpec, more = 0): [number, number] {
  const area = more + 36 + (spec.flavor === 'research' ? 16 : 20) * Math.max(1, spec.agents)
  const aspect = 0.75 + hashOf(spec.name, 1) * 0.7
  let along = Math.round(Math.sqrt(area * aspect))
  let out = Math.round(area / along)
  along = Math.max(7, along)
  out = Math.max(6, out)
  return [along, out]
}

/** The key of the edge between a tile and its neighbour along +a (`a`) or +b (`b`). */
export function edgeKey(a: number, b: number, dir: 'a' | 'b'): string {
  return `${a},${b},${dir}`
}

/** A rectangle along a side of Shiori's area (u along it, v outwards), in tiles round (0, 0). */
function place(side: Side, u0: number, u1: number, v0: number, v1: number): [number, number, number, number] {
  const lo = -Math.floor(HALL / 2) - AISLE
  const hi = lo + HALL + 2 * AISLE
  switch (side) {
    case '-b':
      return [u0, lo - v1, u1, lo - v0]
    case '+b':
      return [u0, hi + v0, u1, hi + v1]
    case '-a':
      return [lo - v1, u0, lo - v0, u1]
    case '+a':
      return [hi + v0, u0, hi + v1, u1]
  }
}

/** `margin` is the ground left round the building, in tiles: the city around it. */
export function makePlan(offices: OfficeSpec[], more: Record<string, number> = {}, margin = 8): Plan {
  const everyone = offices.reduce((n, o) => n + o.agents, 0)
  const common = Math.max(2, Math.round(everyone / 5))
  const lounge = { ...LOUNGE, agents: common }
  const games = { ...GAMES, agents: common + 1 }
  const specs = [...offices, lounge, games]
  const sized = specs.map((spec) => ({ spec, size: roomSize(spec, more[spec.name] ?? 0) })).sort((x, y) => y.size[0] * y.size[1] - x.size[0] * x.size[1])
  // The biggest areas first, each on the side that is shortest so far: the lab stays round,
  // and the back sides fill first, so big areas never hide small ones.
  const sides: Side[] = ['-b', '-a', '+a', '+b']
  const bySide = new Map<Side, typeof sized>(sides.map((s) => [s, []]))
  const length = (s: Side) => bySide.get(s)!.reduce((n, x) => n + x.size[0], 0)
  for (const r of sized) bySide.get(sides.reduce((best, s) => (length(s) < length(best) ? s : best), sides[0]))!.push(r)

  type Raw = Omit<Space, 'id'>
  const raw: Raw[] = []
  const lo = -Math.floor(HALL / 2)
  // Free floor first: the walkway round Shiori's area.
  raw.push({ kind: 'floor', name: '', accent: '#9aa6b4', flavor: 'data', agents: 0, a0: lo - AISLE, b0: lo - AISLE, a1: lo + HALL + AISLE, b1: lo + HALL + AISLE, side: '-b' })
  raw.push({ kind: 'hall', name: 'Shiori', accent: '#46ffb0', flavor: 'data', agents: 0, a0: lo, b0: lo, a1: lo + HALL, b1: lo + HALL, side: '-b' })
  const floors: [number, number, number, number][] = []
  for (const side of sides) {
    const rooms = bySide.get(side)!
    if (!rooms.length) continue
    const total = rooms.reduce((s, r) => s + r.size[0], 0) + (rooms.length - 1) * AISLE
    const shift = Math.round((hashOf(rooms[0].spec.name, 2) - 0.5) * 4)
    let u = Math.round(-total / 2) + shift
    rooms.forEach(({ spec, size }, i) => {
      const [along, out] = size
      // Where two sides meet at a corner, an area that would overlap another steps outwards,
      // with free floor leading to it.
      let v = 0
      const hits = (r: [number, number, number, number]) => raw.some((x) => x.kind !== 'floor' && x.kind !== 'hall' && r[0] < x.a1 && x.a0 < r[2] && r[1] < x.b1 && x.b0 < r[3])
      while (hits(place(side, u - 1, u + along + 1, v, v + out))) v++
      if (v > 0) floors.push(place(side, u, u + along, 0, v))
      const [a0, b0, a1, b1] = place(side, u, u + along, v, v + out)
      raw.push({ kind: spec === lounge ? 'lounge' : spec === games ? 'games' : 'office', name: spec.name, accent: spec.accent, flavor: spec.flavor, agents: spec.agents, a0, b0, a1, b1, side })
      // A walkway between two areas on the same side, as deep as the shallower of the two.
      if (i < rooms.length - 1) floors.push(place(side, u + along, u + along + AISLE, 0, Math.min(out, rooms[i + 1].size[1])))
      u += along + AISLE
    })
  }
  for (const [a0, b0, a1, b1] of floors) raw.push({ kind: 'floor', name: '', accent: '#9aa6b4', flavor: 'data', agents: 0, a0, b0, a1, b1, side: '-b' })

  const minA = Math.min(...raw.map((s) => s.a0))
  const minB = Math.min(...raw.map((s) => s.b0))
  const maxA = Math.max(...raw.map((s) => s.a1))
  const maxB = Math.max(...raw.map((s) => s.b1))
  const offA = margin - minA
  const offB = margin - minB
  const w = maxA - minA + 2 * margin
  const h = maxB - minB + 2 * margin
  const spaces: Space[] = raw.map((s, id) => ({ ...s, id, a0: s.a0 + offA, a1: s.a1 + offA, b0: s.b0 + offB, b1: s.b1 + offB }))
  const cells = new Int16Array(w * h).fill(-1)
  // Floor under everything, then the areas over it.
  for (const s of [...spaces.filter((x) => x.kind === 'floor'), ...spaces.filter((x) => x.kind !== 'floor')])
    for (let b = s.b0; b < s.b1; b++) for (let a = s.a0; a < s.a1; a++) cells[b * w + a] = s.id
  // Every area opens towards Shiori: three tiles in the middle of the side that faces her.
  const doors = new Set<string>()
  for (const s of spaces) {
    if (s.kind !== 'office' && s.kind !== 'lounge' && s.kind !== 'games') continue
    const mid = (x0: number, x1: number) => Math.floor((x0 + x1) / 2) - 1
    for (let k = 0; k < 3; k++) {
      if (s.side === '-b') doors.add(edgeKey(mid(s.a0, s.a1) + k, s.b1 - 1, 'b'))
      if (s.side === '+b') doors.add(edgeKey(mid(s.a0, s.a1) + k, s.b0 - 1, 'b'))
      if (s.side === '-a') doors.add(edgeKey(s.a1 - 1, mid(s.b0, s.b1) + k, 'a'))
      if (s.side === '+a') doors.add(edgeKey(s.a0 - 1, mid(s.b0, s.b1) + k, 'a'))
    }
  }
  return { w, h, spaces, cells, doors, hall: spaces[1], box: [minA + offA, minB + offB, maxA + offA, maxB + offB] }
}

/** The space at a tile, or null outside. */
export function spaceAt(plan: Plan, a: number, b: number): Space | null {
  if (a < 0 || b < 0 || a >= plan.w || b >= plan.h) return null
  const id = plan.cells[b * plan.w + a]
  return id < 0 ? null : plan.spaces[id]
}

/** What stands on the edge between a tile and its neighbour: nothing, the building's wall, or a glass partition. */
export function edgeKind(plan: Plan, a: number, b: number, dir: 'a' | 'b'): 'none' | 'wall' | 'glass' {
  const here = spaceAt(plan, a, b)
  const there = spaceAt(plan, dir === 'a' ? a + 1 : a, dir === 'b' ? b + 1 : b)
  if (!here && !there) return 'none'
  if (!here || !there) return 'wall'
  if (here.id === there.id) return 'none'
  const open = (s: Space) => s.kind === 'floor' || s.kind === 'hall'
  if (open(here) && open(there)) return 'none'
  return plan.doors.has(edgeKey(a, b, dir)) ? 'none' : 'glass'
}

/** Whether something stands between a tile and its neighbour along +a or +b: nobody walks through. */
export function wallBetween(plan: Plan, a: number, b: number, dir: 'a' | 'b'): boolean {
  return edgeKind(plan, a, b, dir) !== 'none'
}
