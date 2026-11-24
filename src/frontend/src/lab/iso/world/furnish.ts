import { hashOf, makePlan, wallBetween, type OfficeSpec, type Plan, type Space } from './layout'

/**
 * What goes in each room, decided from what the room is: how many agents work there and what
 * kind of work (code, research, creative, data). Things that stand against a wall go along the
 * room's back walls; desks go in rows in the middle, each with a seat in front; doors and the
 * tile inside them stay free, and one free tile is kept round every desk so people can pass.
 * Positions are in floor units (eight to a tile).
 */

export type ItemKind = 'bench' | 'corner' | 'holo' | 'microscope' | 'capsule' | 'rack' | 'board' | 'coffee' | 'sofa' | 'plant' | 'chair' | 'totem'

export interface Item {
  kind: ItemKind
  /** Floor origin, in floor units. */
  a: number
  b: number
  /** Along b instead of a (the drawing is mirrored). */
  flip: boolean
  room: number
  accent: string
  seed: number
  variant?: string
}

/** Where someone sits or stands to use something, and which way they face. */
export interface Spot {
  a: number
  b: number
  face: 'NE' | 'NW' | 'SE' | 'SW'
  room: number
  use: 'seat' | 'board' | 'coffee' | 'sofa' | 'shiori'
}

/** Footprints in tiles (along a, along b) when not mirrored, and in floor units. */
export const FOOT: Record<ItemKind, { tiles: [number, number]; units: [number, number] }> = {
  bench: { tiles: [5, 2], units: [40, 12] },
  corner: { tiles: [4, 4], units: [30, 26] },
  holo: { tiles: [3, 3], units: [18, 18] },
  microscope: { tiles: [4, 2], units: [30, 12] },
  capsule: { tiles: [2, 2], units: [12, 12] },
  rack: { tiles: [2, 2], units: [10, 10] },
  board: { tiles: [4, 1], units: [28, 6] },
  coffee: { tiles: [4, 2], units: [26, 9] },
  sofa: { tiles: [4, 2], units: [26, 10] },
  plant: { tiles: [1, 1], units: [6, 6] },
  chair: { tiles: [1, 1], units: [6, 6] },
  totem: { tiles: [1, 2], units: [4, 12] },
}

export interface Furnished {
  items: Item[]
  spots: Spot[]
  /** Tiles nobody can walk on: furniture. */
  blocked: Uint8Array
}

const T = 8

/**
 * Plans and furnishes the lab: a room whose desks do not seat everybody is made bigger and the
 * lab planned again, until all have a seat.
 */
export function planLab(offices: OfficeSpec[]): { plan: Plan; furnished: Furnished } {
  const more: Record<string, number> = {}
  for (let round = 0; ; round++) {
    const plan = makePlan(offices, more)
    const furnished = furnish(plan)
    const short = plan.spaces.filter((s) => s.kind === 'office' && furnished.spots.filter((p) => p.use === 'seat' && p.room === s.id).length < s.agents)
    if (!short.length || round >= 6) return { plan, furnished }
    for (const s of short) more[s.name] = (more[s.name] ?? 0) + 14
  }
}

export function furnish(plan: Plan): Furnished {
  const items: Item[] = []
  const spots: Spot[] = []
  const used = new Uint8Array(plan.w * plan.h)
  const blocked = new Uint8Array(plan.w * plan.h)

  for (const room of plan.spaces) {
    if (room.kind === 'office' || room.kind === 'lounge') furnishRoom(plan, room, items, spots, used, blocked)
  }
  // Around Shiori: places to stand and ask her something, a few steps from the dome.
  const hall = plan.hall
  const ca = (hall.a0 + hall.a1) / 2
  const cb = (hall.b0 + hall.b1) / 2
  for (let k = 0; k < 12; k++) {
    const th = (k / 12) * Math.PI * 2
    const a = ca + Math.cos(th) * 5.4
    const b = cb + Math.sin(th) * 5.4
    const ta = Math.floor(a)
    const tb = Math.floor(b)
    if (ta < hall.a0 || tb < hall.b0 || ta >= hall.a1 || tb >= hall.b1) continue
    const face = Math.abs(Math.cos(th)) > Math.abs(Math.sin(th)) ? (Math.cos(th) > 0 ? 'NW' : 'SE') : Math.sin(th) > 0 ? 'NE' : 'SW'
    spots.push({ a: ta * T + 4, b: tb * T + 4, face, room: hall.id, use: 'shiori' })
  }
  return { items, spots, blocked }
}

function furnishRoom(plan: Plan, room: Space, items: Item[], spots: Spot[], used: Uint8Array, blocked: Uint8Array) {
  const seed = Math.floor(hashOf(room.name, 9) * 1e6)
  const free = (a: number, b: number) => a >= room.a0 && b >= room.b0 && a < room.a1 && b < room.b1 && !used[b * plan.w + a]
  const mark = (a0: number, b0: number, w: number, h: number, value = 1) => {
    for (let b = b0; b < b0 + h; b++) for (let a = a0; a < a0 + w; a++) if (a >= 0 && b >= 0 && a < plan.w && b < plan.h) used[b * plan.w + a] = value
  }
  const fits = (a0: number, b0: number, w: number, h: number) => {
    for (let b = b0; b < b0 + h; b++) for (let a = a0; a < a0 + w; a++) if (!free(a, b)) return false
    return true
  }
  const block = (kind: ItemKind, a: number, b: number, flip: boolean) => {
    if (kind === 'corner') {
      // An L: its two arms, not the corner between them, where its seat is.
      rect(a, b, ...((flip ? [12, 30] : [30, 12]) as [number, number]))
      rect(a, b, ...((flip ? [26, 12] : [12, 26]) as [number, number]))
      return
    }
    const [ua, ub] = FOOT[kind].units
    rect(a, b, ...((flip ? [ub, ua] : [ua, ub]) as [number, number]))
  }
  const rect = (a: number, b: number, da: number, db: number) => {
    for (let tb = Math.floor(b / T); tb <= Math.floor((b + db - 0.01) / T); tb++)
      for (let ta = Math.floor(a / T); ta <= Math.floor((a + da - 0.01) / T); ta++) {
        // A tile is taken only if the piece covers its middle.
        const cx = ta * T + 4
        const cy = tb * T + 4
        if (cx >= a && cx < a + da && cy >= b && cy < b + db) blocked[tb * plan.w + ta] = 1
      }
  }
  const put = (kind: ItemKind, ta: number, tb: number, flip = false, variant?: string) => {
    const a = ta * T + (flip ? 1 : 0)
    const b = tb * T + (flip ? 0 : 1)
    items.push({ kind, a, b, flip, room: room.id, accent: room.accent, seed: seed + items.length * 7, variant })
    block(kind, a, b, flip)
  }

  // Doors stay clear, with the tile inside them.
  for (let b = room.b0; b < room.b1; b++) {
    for (let a = room.a0; a < room.a1; a++) {
      for (const [na, nb, dir, ea, eb] of [
        [a + 1, b, 'a', a, b],
        [a - 1, b, 'a', a - 1, b],
        [a, b + 1, 'b', a, b],
        [a, b - 1, 'b', a, b - 1],
      ] as const) {
        const outside = na < room.a0 || nb < room.b0 || na >= room.a1 || nb >= room.b1
        if (outside && !wallBetween(plan, ea, eb, dir)) {
          mark(a, b, 1, 1)
        }
      }
    }
  }

  // Against the back walls: what the kind of work needs.
  const n = Math.max(1, room.agents)
  const wallItems: ItemKind[] =
    room.kind === 'lounge'
      ? ['coffee', 'plant', 'board']
      : room.flavor === 'code'
        ? ['board', ...Array<ItemKind>(1 + Math.floor(n / 3)).fill('rack'), 'plant']
        : room.flavor === 'research'
          ? [...Array<ItemKind>(2 + Math.floor(n / 2)).fill('capsule'), 'board']
          : room.flavor === 'creative'
            ? ['board', 'totem', 'plant', 'board']
            : ['board', 'totem', 'totem', 'plant']
  const boards = ['math', 'diagram', 'graph']
  let wall = 0
  // Along the wall at the back (low b), facing +b; then along the other back wall, facing +a.
  for (let a = room.a0 + 1; a < room.a1 - 1 && wall < wallItems.length;) {
    const kind = wallItems[wall]
    const [w, h] = FOOT[kind].tiles
    if (a + w <= room.a1 - 1 && fits(a, room.b0, w, h + 1)) {
      const variant = kind === 'board' ? boards[(seed + wall) % 3] : undefined
      put(kind, a, room.b0, false, variant)
      mark(a, room.b0, w, h)
      if (kind === 'board') spots.push({ a: (a + 2) * T, b: (room.b0 + 2) * T + 2, face: 'NE', room: room.id, use: 'board' })
      if (kind === 'coffee')
        spots.push({ a: (a + 2) * T, b: (room.b0 + 3) * T, face: 'NE', room: room.id, use: 'coffee' }, { a: (a + 3) * T + 4, b: (room.b0 + 3) * T, face: 'NW', room: room.id, use: 'coffee' })
      a += w + 1
      wall++
    } else a++
  }
  for (let b = room.b0 + 2; b < room.b1 - 1 && wall < wallItems.length;) {
    const kind = wallItems[wall]
    const [w, h] = FOOT[kind].tiles
    if (b + w <= room.b1 - 1 && fits(room.a0, b, h + 1, w)) {
      const variant = kind === 'board' ? boards[(seed + wall) % 3] : undefined
      put(kind, room.a0, b, true, variant)
      mark(room.a0, b, h, w)
      if (kind === 'board') spots.push({ a: (room.a0 + 2) * T + 2, b: (b + 2) * T, face: 'NW', room: room.id, use: 'board' })
      b += w + 1
      wall++
    } else b++
  }

  // Desks in rows, each with a free row in front for the seats and a free tile around.
  const first: ItemKind = room.kind === 'lounge' ? 'sofa' : room.flavor === 'code' ? 'bench' : room.flavor === 'research' ? 'microscope' : 'corner'
  let toSeat = room.kind === 'lounge' ? 1 : n
  // Facing +b first (people sit with their back to you); where that does not fit, along b; if
  // the room's own kind of desk does not fit everyone, smaller lab benches take the rest.
  for (const [desk, flip] of [
    [first, false],
    [first, true],
    ['microscope', false],
    ['microscope', true],
  ] as [ItemKind, boolean][]) {
    if (room.kind === 'lounge' && desk !== 'sofa') continue
    const [dw, dh] = FOOT[desk].tiles
    const [w, h] = flip ? [dh, dw] : [dw, dh]
    for (let b = room.b0 + 2; b + h + 1 <= room.b1 && toSeat > 0; b++) {
      for (let a = room.a0 + 1; a + w + 1 <= room.a1 && toSeat > 0; a++) {
        // The desk, its seat row in front, a free tile at its ends to pass.
        if (!fits(a - 1, b, w + 2 + (flip ? 1 : 0), h + (flip ? 0 : 1))) continue
        put(desk, a, b, flip)
        mark(a - 1, b, w + 2 + (flip ? 1 : 0), h + (flip ? 0 : 1))
        const ua = a * T
        const ub = b * T
        // A seat in front of the desk: +b when it faces +b, +a when it is turned.
        const seat = (along: number, out: number) =>
          spots.push(
            flip
              ? { a: ua + out, b: ub + along, face: 'NW', room: room.id, use: desk === 'sofa' ? 'sofa' : 'seat' }
              : { a: ua + along, b: ub + out, face: 'NE', room: room.id, use: desk === 'sofa' ? 'sofa' : 'seat' },
          )
        if (desk === 'bench') for (let k = 0; k < 3 && toSeat > 0; k++, toSeat--) seat(8 + k * 13, 20)
        else if (desk === 'microscope') for (let k = 0; k < 2 && toSeat > 0; k++, toSeat--) seat(8 + k * 14, 20)
        else if (desk === 'corner') {
          seat(20, 18)
          toSeat--
        } else {
          seat(12, 18)
          toSeat--
        }
        a += w + 1
      }
    }
  }
  // Creative rooms get a hologram table to gather round; every room some plants in free corners.
  if (room.flavor === 'creative' && room.kind === 'office') {
    for (let b = room.b1 - 4; b > room.b0 + 2; b--) {
      const a = room.a1 - 4
      if (fits(a - 1, b - 1, 5, 5)) {
        put('holo', a, b)
        mark(a - 1, b - 1, 5, 5)
        break
      }
    }
  }
  for (const [a, b] of [
    [room.a1 - 1, room.b0],
    [room.a0, room.b1 - 1],
    [room.a1 - 1, room.b1 - 1],
  ]) {
    if (fits(a, b, 1, 1) && hashOf(room.name, a * 31 + b) > 0.3) {
      put('plant', a, b)
      mark(a, b, 1, 1)
    }
  }
}

/** Tile walking: four directions, no walls crossed, no furniture stepped on. A plain A*. */
export function findPath(plan: Plan, blocked: Uint8Array, from: [number, number], to: [number, number]): [number, number][] | null {
  const W = plan.w
  const ok = (a: number, b: number) => a >= 0 && b >= 0 && a < W && b < plan.h && plan.cells[b * W + a] >= 0 && !blocked[b * W + a]
  const [sa, sb] = from
  const [ga, gb] = to
  if (!ok(ga, gb)) return null
  const start = sb * W + sa
  const goal = gb * W + ga
  const g = new Map<number, number>([[start, 0]])
  const came = new Map<number, number>()
  const open: [number, number][] = [[Math.abs(sa - ga) + Math.abs(sb - gb), start]]
  const closed = new Set<number>()
  while (open.length) {
    let best = 0
    for (let i = 1; i < open.length; i++) if (open[i][0] < open[best][0]) best = i
    const [, cur] = open.splice(best, 1)[0]
    if (cur === goal) {
      const path: [number, number][] = []
      for (let k: number | undefined = cur; k !== undefined; k = came.get(k)) path.unshift([k % W, Math.floor(k / W)])
      return path
    }
    if (closed.has(cur)) continue
    closed.add(cur)
    const a = cur % W
    const b = Math.floor(cur / W)
    const steps: [number, number, boolean][] = [
      [a + 1, b, !wallBetween(plan, a, b, 'a')],
      [a - 1, b, a > 0 && !wallBetween(plan, a - 1, b, 'a')],
      [a, b + 1, !wallBetween(plan, a, b, 'b')],
      [a, b - 1, b > 0 && !wallBetween(plan, a, b - 1, 'b')],
    ]
    for (const [na, nb, open_] of steps) {
      if (!open_ || !ok(na, nb)) continue
      const k = nb * W + na
      // Turning costs a little: paths keep straight runs, as people walk.
      const turn = came.has(cur) && (came.get(cur)! % W === a) !== (na === a) ? 0.3 : 0
      const cost = g.get(cur)! + 1 + turn
      if (cost < (g.get(k) ?? Infinity)) {
        g.set(k, cost)
        came.set(k, cur)
        open.push([cost + Math.abs(na - ga) + Math.abs(nb - gb), k])
      }
    }
  }
  return null
}
