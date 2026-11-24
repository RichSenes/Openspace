/**
 * The lab's floor plan, made from the offices someone sets up: nothing is drawn by hand. Shiori's
 * hall sits in the middle; every office becomes a room sized by how many agents work in it,
 * with a shape of its own (seeded by its name, so the same setup always gives the same lab),
 * reached from the hall by a corridor. Rooms go round the hall, the biggest first; a lounge is
 * added for everybody. The plan is a grid of tiles: each tile belongs to one space or to the
 * outside, and walls stand wherever two spaces meet, except at doors.
 */

export type Flavor = 'code' | 'research' | 'creative' | 'data'

export interface OfficeSpec {
  name: string
  accent: string
  agents: number
  flavor: Flavor
}

export type SpaceKind = 'hall' | 'office' | 'corridor' | 'lounge'

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
  /** Which way it opens towards the hall: the side of the hall it stands on. */
  side: Side
}

export type Side = '-a' | '+a' | '-b' | '+b'

export interface Plan {
  /** Size of the grid in tiles, the ground around included. */
  w: number
  h: number
  spaces: Space[]
  /** For each tile, the id of its space, or -1 outside. */
  cells: Int16Array
  /** Tile edges with a door: walls are not built there. Keyed by edgeKey. */
  doors: Set<string>
  hall: Space
}

/** The ground left around the building, in tiles. */
export const MARGIN = 7
const HALL = 11
const CORRIDOR = 2
const LOUNGE: OfficeSpec = { name: 'Lounge', accent: '#c48bff', agents: 2, flavor: 'data' }

/** A repeatable number in [0, 1) from a name. */
export function hashOf(text: string, salt = 0): number {
  let h = 2166136261 ^ salt
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619)
  h ^= h >>> 13
  h = Math.imul(h, 1274126177)
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296
}

/** A room's size in tiles: more agents, more room; its proportions come from its name. */
export function roomSize(spec: OfficeSpec, more = 0): [number, number] {
  const area = more + 40 + (spec.flavor === 'research' ? 16 : 22) * Math.max(1, spec.agents)
  const aspect = 0.7 + hashOf(spec.name, 1) * 0.75
  let along = Math.round(Math.sqrt(area * aspect))
  let out = Math.round(area / along)
  along = Math.max(6, along)
  out = Math.max(5, out)
  return [along, out]
}

/** The key of the edge between a tile and its neighbour along +a (`a`) or +b (`b`). */
export function edgeKey(a: number, b: number, dir: 'a' | 'b'): string {
  return `${a},${b},${dir}`
}

interface Local {
  u0: number
  u1: number
  v0: number
  v1: number
}

/** A rectangle given along a side of the hall (u along it, v outwards) as floor tiles around (0, 0). */
function place(side: Side, r: Local): [number, number, number, number] {
  const h = HALL / 2
  const lo = Math.floor(-h)
  const hi = lo + HALL
  switch (side) {
    case '-b':
      return [r.u0, lo - r.v1, r.u1, lo - r.v0]
    case '+b':
      return [r.u0, hi + r.v0, r.u1, hi + r.v1]
    case '-a':
      return [lo - r.v1, r.u0, lo - r.v0, r.u1]
    case '+a':
      return [hi + r.v0, r.u0, hi + r.v1, r.u1]
  }
}

export function makePlan(offices: OfficeSpec[], more: Record<string, number> = {}): Plan {
  const specs = [...offices, LOUNGE]
  const sized = specs.map((spec) => ({ spec, size: roomSize(spec, more[spec.name] ?? 0) })).sort((x, y) => y.size[0] * y.size[1] - x.size[0] * x.size[1])
  // The biggest rooms take the back sides, so they never hide the smaller ones.
  const sides: Side[] = ['-b', '-a', '+a', '+b']
  const bySide = new Map<Side, typeof sized>(sides.map((s) => [s, []]))
  sized.forEach((r, i) => bySide.get(sides[i % 4])!.push(r))

  type Raw = Omit<Space, 'id'>
  const raw: Raw[] = []
  const lo = Math.floor(-HALL / 2)
  raw.push({ kind: 'hall', name: 'Shiori', accent: '#46ffb0', flavor: 'data', agents: 0, a0: lo, b0: lo, a1: lo + HALL, b1: lo + HALL, side: '-b' })
  for (const side of sides) {
    const rooms = bySide.get(side)!
    if (!rooms.length) continue
    const total = rooms.reduce((s, r) => s + r.size[0], 0)
    // Off centre a little, by the first room's name: no two labs look alike.
    const shift = Math.round((hashOf(rooms[0].spec.name, 2) - 0.5) * 4)
    let u = Math.round(-total / 2) + shift
    const length = CORRIDOR + Math.round(hashOf(rooms[0].spec.name, 3) * 2)
    // The corridor from the hall.
    const [ca0, cb0, ca1, cb1] = place(side, { u0: -1, u1: 1, v0: 0, v1: length })
    raw.push({ kind: 'corridor', name: '', accent: '#9aa6b4', flavor: 'data', agents: 0, a0: ca0, b0: cb0, a1: ca1, b1: cb1, side })
    let v0 = length
    if (rooms.length > 1 || u > -1 || u + total < 1) {
      // A cross corridor along the side, so every room has its own door.
      const [xa0, xb0, xa1, xb1] = place(side, { u0: Math.min(u, -1), u1: Math.max(u + total, 1), v0: length, v1: length + 2 })
      raw.push({ kind: 'corridor', name: '', accent: '#9aa6b4', flavor: 'data', agents: 0, a0: xa0, b0: xb0, a1: xa1, b1: xb1, side })
      v0 = length + 2
    }
    for (const { spec, size } of rooms) {
      const [along, out] = size
      const [a0, b0, a1, b1] = place(side, { u0: u, u1: u + along, v0, v1: v0 + out })
      raw.push({ kind: spec === LOUNGE ? 'lounge' : 'office', name: spec.name, accent: spec.accent, flavor: spec.flavor, agents: spec.agents, a0, b0, a1, b1, side })
      u += along
    }
  }

  // Into a grid with the ground around it.
  const minA = Math.min(...raw.map((s) => s.a0)) - MARGIN
  const minB = Math.min(...raw.map((s) => s.b0)) - MARGIN
  const maxA = Math.max(...raw.map((s) => s.a1)) + MARGIN
  const maxB = Math.max(...raw.map((s) => s.b1)) + MARGIN
  const w = maxA - minA
  const h = maxB - minB
  const spaces: Space[] = raw.map((s, id) => ({ ...s, id, a0: s.a0 - minA, a1: s.a1 - minA, b0: s.b0 - minB, b1: s.b1 - minB }))
  const cells = new Int16Array(w * h).fill(-1)
  for (const s of spaces) for (let b = s.b0; b < s.b1; b++) for (let a = s.a0; a < s.a1; a++) if (cells[b * w + a] < 0 || s.kind !== 'corridor') cells[b * w + a] = s.id

  // Doors: corridors open fully onto the hall and onto each other; a room gets a two-tile door
  // in the middle of what it shares with a corridor.
  const doors = new Set<string>()
  const kindOf = (id: number) => (id < 0 ? null : spaces[id].kind)
  const shared = new Map<string, string[]>()
  for (let b = 0; b < h; b++) {
    for (let a = 0; a < w; a++) {
      const here = cells[b * w + a]
      if (here < 0) continue
      for (const dir of ['a', 'b'] as const) {
        const na = dir === 'a' ? a + 1 : a
        const nb = dir === 'b' ? b + 1 : b
        if (na >= w || nb >= h) continue
        const there = cells[nb * w + na]
        if (there < 0 || there === here) continue
        const kinds = [kindOf(here), kindOf(there)]
        const key = edgeKey(a, b, dir)
        if (kinds.every((k) => k === 'corridor' || k === 'hall')) doors.add(key)
        else if (kinds.includes('corridor')) {
          const pair = `${Math.min(here, there)}-${Math.max(here, there)}`
          shared.set(pair, [...(shared.get(pair) ?? []), key])
        }
      }
    }
  }
  for (const keys of shared.values()) {
    const mid = Math.floor(keys.length / 2)
    for (const k of keys.slice(Math.max(0, mid - 1), mid + 1)) doors.add(k)
  }
  return { w, h, spaces, cells, doors, hall: spaces[0] }
}

/** The space at a tile, or null outside. */
export function spaceAt(plan: Plan, a: number, b: number): Space | null {
  if (a < 0 || b < 0 || a >= plan.w || b >= plan.h) return null
  const id = plan.cells[b * plan.w + a]
  return id < 0 ? null : plan.spaces[id]
}

/** Whether a wall stands between a tile and its neighbour along +a or +b. */
export function wallBetween(plan: Plan, a: number, b: number, dir: 'a' | 'b'): boolean {
  const here = spaceAt(plan, a, b)
  const there = spaceAt(plan, dir === 'a' ? a + 1 : a, dir === 'b' ? b + 1 : b)
  if (!here && !there) return false
  if (here && there && here.id === there.id) return false
  return !plan.doors.has(edgeKey(a, b, dir))
}
