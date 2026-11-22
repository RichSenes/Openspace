import type { AgentState } from '../art/agentStates'
import { FURNITURE } from '../art/fanPack'
import type { PropName } from '../art/sheets/props'
import { STATION } from '../art/station'
import { WALKER, type Facing } from '../art/walker'
import { chance, type Spot } from '../activity/walks'
import { DOOR, inBuilding, TILE, underRoom, WALL, type BuildingLayout } from './layout'

/*
 * Shiori in the building: she walks the corridors and the rooms through their doors,
 * stops beside a desk whose task just changed (started, failed, needs the person, to
 * review) to talk for a few seconds, now and then visits the brain's place, and leaves.
 * Pure logic, driven by the clock and the desks' states, so redraws agree.
 */

/** What happened at a desk that brings Shiori over. */
export type ShioriEvent = 'started' | 'failed' | 'waiting' | 'review'

/** What she is doing now. */
export type ShioriMode = 'walking' | 'talking' | 'visiting' | 'idle'

/** Shiori as drawn now: the top left corner of her 24 by 32 frame, like any standing agent. */
export interface ShioriNow {
  x: number
  y: number
  facing: Facing
  /** Frame of the legs: still at 0. */
  step: number
  mode: ShioriMode
  /** While she talks at a desk: which one, and about what (the icon in her bubble). */
  talk?: { deskId: string; event: ShioriEvent }
  /** Who she is walking to, if she has news for a desk. */
  heading?: string
}

/** A tile of the world: column and row. */
export interface Tile {
  x: number
  y: number
}

/** A place she can stand, with the way she faces there and the tile it lies on. */
export interface StandSpot extends Spot {
  tile: Tile
  facing: Facing
}

/** Where she can walk and where she goes: built from the building's layout. */
export interface ShioriWorld {
  cols: number
  rows: number
  /** Whether each tile, row by row, can be walked on. */
  walkable: readonly boolean[]
  /** What each tile belongs to: a room's id, or the corridor. */
  region: readonly string[]
  /** Passages between two tiles of different regions: the doors, as "x,y|x,y" with the smaller first. */
  doors: ReadonlySet<string>
  /** Beside each desk, left then right, facing it. */
  desks: ReadonlyMap<string, readonly StandSpot[]>
  /** In front of what holds the office's knowledge (shelves, boards, cabinets); a quiet corner when there is none. */
  brain: readonly StandSpot[]
  /** Somewhere to stand in each room and along the corridor. */
  wander: readonly StandSpot[]
}

/** Walking pace, in pixels a second: a little quicker than the agents, she has places to be. */
export const SHIORI_PACE = 34
/** How long she talks at a desk, visits the brain, and stands about. */
export const TALK = 4500
export const VISIT = 7000
export const LINGER = 5000
/** News older than this is not worth the walk any more. */
export const STALE = 90_000
/** Every this many outings without news, the next goes to the brain. */
export const BRAIN_EVERY = 3

const HALL = 'hall'
/** Who needs her first: someone waiting for the person, then a failure, a review, a start. */
const URGENCY: Record<ShioriEvent, number> = { waiting: 0, failed: 1, review: 2, started: 3 }
const BUSY: ReadonlySet<AgentState> = new Set(['working', 'reading'])
/** What holds knowledge: the brain's place in an office that has no brain of its own drawn. */
const KNOWLEDGE: ReadonlySet<PropName> = new Set(['bookshelf', 'whiteboard', 'filingCabinet'])

/** The news in a desk's change of state, if any. */
export function eventOf(before: AgentState, now: AgentState): ShioriEvent | null {
  if (before === now) return null
  if (BUSY.has(now)) return BUSY.has(before) ? null : 'started'
  if (now === 'failed') return 'failed'
  if (now === 'waiting') return 'waiting'
  if (now === 'done') return 'review'
  return null
}

/** Where a standing frame goes for its feet to be on a tile. */
const frameOn = (tile: Tile): Spot => ({ x: tile.x * TILE + TILE / 2 - WALKER.width / 2, y: tile.y * TILE + 13 - (WALKER.height - 1) })
const tileUnder = (x: number, y: number): Tile => ({ x: Math.floor((x + WALKER.width / 2) / TILE), y: Math.floor((y + WALKER.height - 1) / TILE) })
const key = (a: Tile, b: Tile) => (a.y < b.y || (a.y === b.y && a.x <= b.x) ? `${a.x},${a.y}|${b.x},${b.y}` : `${b.x},${b.y}|${a.x},${a.y}`)

/**
 * The building as Shiori walks it, in tiles: room floors and the corridor are open, back
 * walls, desks and standing furniture are not, and two spaces meet only through a door.
 */
export function shioriWorld(layout: BuildingLayout): ShioriWorld {
  const cols = layout.width
  const rows = layout.height
  const walkable = new Array<boolean>(cols * rows).fill(false)
  const region = new Array<string>(cols * rows).fill('')
  const at = (x: number, y: number) => y * cols + x
  const inGrid = (x: number, y: number) => x >= 0 && y >= 0 && x < cols && y < rows

  // The corridor: the building's tiles below its back walls.
  for (let x = 0; x < cols; x++) {
    let fromWall = 0
    for (let y = 0; y < rows; y++) {
      if (!inBuilding(layout, x, y)) {
        fromWall = 0
        continue
      }
      region[at(x, y)] = HALL
      walkable[at(x, y)] = fromWall >= WALL
      fromWall++
    }
  }
  const doors = new Set<string>()
  for (const room of layout.rooms) {
    for (let y = room.y; y < room.y + room.height; y++)
      for (let x = room.x; x < room.x + room.width; x++) {
        if (!inGrid(x, y)) continue
        region[at(x, y)] = room.id
        walkable[at(x, y)] = y >= room.y + WALL
      }
    const { wall, offset } = room.door
    for (let i = 0; i < DOOR; i++) {
      if (wall === 'Front') doors.add(key({ x: room.x + offset + i, y: room.y + room.height - 1 }, { x: room.x + offset + i, y: room.y + room.height }))
      else if (wall === 'Left') doors.add(key({ x: room.x, y: room.y + WALL + offset + i }, { x: room.x - 1, y: room.y + WALL + offset + i }))
      else if (wall === 'Right') doors.add(key({ x: room.x + room.width - 1, y: room.y + WALL + offset + i }, { x: room.x + room.width, y: room.y + WALL + offset + i }))
      else {
        // A back door opens the wall itself: its tiles become a passage.
        const x = room.x + offset + i
        for (let y = room.y; y < room.y + WALL; y++) if (inGrid(x, y)) walkable[at(x, y)] = true
        doors.add(key({ x, y: room.y }, { x, y: room.y - 1 }))
      }
    }
  }

  // Desks and standing furniture are in the way; only their feet count, not how tall they are.
  const block = (left: number, top: number, width: number, height: number) => {
    for (let y = Math.floor(top / TILE); y <= Math.floor((top + height - 1) / TILE); y++)
      for (let x = Math.floor(left / TILE); x <= Math.floor((left + width - 1) / TILE); x++) if (inGrid(x, y)) walkable[at(x, y)] = false
  }
  for (const room of layout.rooms) {
    for (const d of room.desks) block(d.x + 4, d.y + STATION.deskY + 4, STATION.width - 8, STATION.height - STATION.deskY - 6)
    for (const p of room.props) {
      const f = FURNITURE[p.name]
      if (f.layer) continue
      block(room.x * TILE + p.x + 2, room.y * TILE + p.y + f.sprite.height - 10, f.sprite.width - 4, 8)
    }
  }
  for (const p of layout.hall) {
    const f = FURNITURE[p.name]
    if (f.layer || underRoom(layout, p)) continue
    block(p.x + 2, p.y + f.sprite.height - 10, f.sprite.width - 4, 8)
  }

  const open = (t: Tile) => inGrid(t.x, t.y) && walkable[at(t.x, t.y)]
  const stand = (tile: Tile, facing: Facing): StandSpot => ({ ...frameOn(tile), tile, facing })

  // Beside a desk, where its agent gets out of its chair, facing it: never in front of it.
  // She reaches it from the floor just below, as the gaps between desks are narrower than a tile.
  const desks = new Map<string, StandSpot[]>()
  for (const room of layout.rooms)
    for (const d of room.desks) {
      const y = d.y + STATION.deskY - 14
      const mine = (t: Tile) => region[at(t.x, t.y)] === room.id
      const side = (x: number, facing: Facing): StandSpot[] => {
        const below = { x: Math.floor((x + WALKER.width / 2) / TILE), y: Math.floor((y + WALKER.height - 1) / TILE) + 1 }
        const tile = nearestOpen(below, open, mine)
        const inRoom = x >= room.x * TILE - 4 && x + WALKER.width <= (room.x + room.width) * TILE + 4
        return tile && inRoom ? [{ x, y, tile, facing }] : []
      }
      desks.set(d.id, [...side(d.x - WALKER.width + 2, 'right'), ...side(d.x + STATION.width - 2, 'left')])
    }

  // The brain: in front of the office's shelves and boards, else the corridor's quiet corner.
  const brain: StandSpot[] = []
  const front = (left: number, top: number, width: number, height: number) => {
    const below = { x: Math.floor((left + width / 2) / TILE), y: Math.floor((top + height + 4) / TILE) }
    const tile = inGrid(below.x, below.y) ? nearestOpen(below, open, (t) => region[at(t.x, t.y)] === region[at(below.x, below.y)]) : undefined
    if (tile) brain.push(stand(tile, 'back'))
  }
  for (const room of layout.rooms)
    for (const p of room.props)
      if (KNOWLEDGE.has(p.name)) {
        const s = FURNITURE[p.name].sprite
        front(room.x * TILE + p.x, room.y * TILE + p.y, s.width, s.height)
      }
  for (const p of layout.hall)
    if (KNOWLEDGE.has(p.name) && !underRoom(layout, p)) {
      const s = FURNITURE[p.name].sprite
      front(p.x, p.y, s.width, s.height)
    }
  if (brain.length === 0) {
    const corner = cornerOf(cols, rows, (x, y) => open({ x, y }) && region[at(x, y)] === HALL) ?? cornerOf(cols, rows, (x, y) => open({ x, y }))
    if (corner) brain.push(stand(corner, 'front'))
  }

  // Somewhere in each room, and a few places along the corridor: never over a desk, its name or furniture.
  const stations = [
    ...layout.rooms.flatMap((r) => r.desks).map((d) => ({ x: d.x - 2, y: d.y + 8, width: STATION.width + 4, height: STATION.height })),
    ...layout.rooms.flatMap((r) => r.props.map((p) => ({ x: r.x * TILE + p.x, y: r.y * TILE + p.y, ...size(p.name) }))),
    ...layout.hall.map((p) => ({ x: p.x, y: p.y, ...size(p.name) })),
  ]
  const clear = (t: Tile) => {
    const f = frameOn(t)
    return !stations.some((d) => f.x < d.x + d.width && d.x < f.x + WALKER.width && f.y < d.y + d.height && d.y < f.y + WALKER.height)
  }
  const wander: StandSpot[] = []
  for (const room of layout.rooms) {
    const centre = { x: room.x + room.width / 2, y: room.y + room.height }
    const tiles: Tile[] = []
    for (let y = room.y + WALL; y < room.y + room.height; y++)
      for (let x = room.x; x < room.x + room.width; x++) if (open({ x, y }) && region[at(x, y)] === room.id && clear({ x, y })) tiles.push({ x, y })
    const best = tiles.sort((a, b) => Math.abs(a.x - centre.x) + Math.abs(a.y - centre.y) - (Math.abs(b.x - centre.x) + Math.abs(b.y - centre.y)))[0]
    if (best) wander.push(stand(best, 'front'))
  }
  const hallTiles: Tile[] = []
  for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) if (region[at(x, y)] === HALL && open({ x, y }) && clear({ x, y })) hallTiles.push({ x, y })
  for (let i = 0; i < 3 && hallTiles.length > 0; i++) wander.push(stand(hallTiles[Math.floor(((i + 0.5) / 3) * hallTiles.length)], 'front'))

  return { cols, rows, walkable, region, doors, desks, brain, wander }
}

const size = (name: PropName) => ({ width: FURNITURE[name].sprite.width, height: FURNITURE[name].sprite.height })

function cornerOf(cols: number, rows: number, ok: (x: number, y: number) => boolean): Tile | undefined {
  // The lowest right corner of the corridor: out of the way of the rooms' doors.
  for (let y = rows - 1; y >= 0; y--) for (let x = cols - 1; x >= 0; x--) if (ok(x, y)) return { x, y }
  return undefined
}

function nearestOpen(from: Tile, open: (t: Tile) => boolean, mine: (t: Tile) => boolean): Tile | undefined {
  for (let r = 0; r < 6; r++)
    for (let dy = -r; dy <= r; dy++)
      for (let dx = -r; dx <= r; dx++) {
        const t = { x: from.x + dx, y: from.y + dy }
        if (Math.max(Math.abs(dx), Math.abs(dy)) === r && open(t) && mine(t)) return t
      }
  return undefined
}

/** Whether a step from one tile to its neighbour is allowed: both open, and through a door between two spaces. */
function passable(world: ShioriWorld, a: Tile, b: Tile): boolean {
  if (b.x < 0 || b.y < 0 || b.x >= world.cols || b.y >= world.rows) return false
  if (!world.walkable[b.y * world.cols + b.x]) return false
  const ra = world.region[a.y * world.cols + a.x]
  const rb = world.region[b.y * world.cols + b.x]
  return ra === rb || world.doors.has(key(a, b))
}

/**
 * The shortest way between two tiles, as the tiles where it turns, the first and last
 * included; null when there is none. Straight runs first, so she turns as little as she can.
 */
export function findRoute(world: ShioriWorld, from: Tile, to: Tile): Tile[] | null {
  const index = (t: Tile) => t.y * world.cols + t.x
  if (from.x === to.x && from.y === to.y) return [from]
  const came = new Map<number, number>([[index(from), -1]])
  const queue: Tile[] = [from]
  const steps = [
    [1, 0],
    [-1, 0],
    [0, 1],
    [0, -1],
  ]
  for (let head = 0; head < queue.length; head++) {
    const t = queue[head]
    for (const [dx, dy] of steps) {
      const n = { x: t.x + dx, y: t.y + dy }
      if (came.has(index(n)) || !passable(world, t, n)) continue
      came.set(index(n), index(t))
      if (n.x === to.x && n.y === to.y) {
        const path: Tile[] = []
        for (let i = index(n); i !== -1; i = came.get(i)!) path.push({ x: i % world.cols, y: Math.floor(i / world.cols) })
        return corners(path.reverse())
      }
      queue.push(n)
    }
  }
  return null
}

/** Keeps the first and last tiles and every tile where the way turns. */
function corners(path: Tile[]): Tile[] {
  if (path.length <= 2) return path
  const kept = [path[0]]
  for (let i = 1; i < path.length - 1; i++) {
    const [a, b, c] = [path[i - 1], path[i], path[i + 1]]
    if (b.x - a.x !== c.x - b.x || b.y - a.y !== c.y - b.y) kept.push(b)
  }
  kept.push(path[path.length - 1])
  return kept
}

const lengthOf = (path: readonly Spot[]) => path.slice(1).reduce((sum, p, i) => sum + Math.abs(p.x - path[i].x) + Math.abs(p.y - path[i].y), 0)

/** Where a walk has got to after `distance` pixels, and which way she faces. */
function along(path: readonly Spot[], distance: number): { at: Spot; facing: Facing; done: boolean } {
  let left = distance
  for (let i = 1; i < path.length; i++) {
    const [a, b] = [path[i - 1], path[i]]
    const length = Math.abs(b.x - a.x) + Math.abs(b.y - a.y)
    if (left < length) {
      const t = left / length
      const facing: Facing = Math.abs(b.x - a.x) >= Math.abs(b.y - a.y) ? (b.x > a.x ? 'right' : 'left') : b.y < a.y ? 'back' : 'front'
      return { at: { x: Math.round(a.x + (b.x - a.x) * t), y: Math.round(a.y + (b.y - a.y) * t) }, facing, done: false }
    }
    left -= length
  }
  return { at: path[path.length - 1], facing: 'front', done: true }
}

type Goal = { kind: 'desk'; deskId: string } | { kind: 'brain' } | { kind: 'wander' }

type Plan =
  | { mode: 'walking'; path: Spot[]; start: number; goal: Goal; spot: StandSpot }
  | { mode: 'talking' | 'visiting' | 'idle'; spot: StandSpot; since: number; until: number; talk?: { deskId: string; event: ShioriEvent } }

export interface ShioriOptions {
  /** False for people who prefer less motion: she appears where she goes instead of walking there. */
  motion?: boolean
}

/**
 * Shiori's day in the building. Each update reads the desks' states: a change worth
 * telling becomes news for that desk, and she walks over (the most urgent first),
 * talks there for a while and moves on: to more news, to the brain every few outings,
 * or somewhere along the corridor or in a room.
 */
export class ShioriStage {
  private readonly motion: boolean
  private readonly states = new Map<string, AgentState>()
  private readonly news = new Map<string, { event: ShioriEvent; at: number }>()
  private plan?: Plan
  private outings = 0

  constructor(options: ShioriOptions = {}) {
    this.motion = options.motion ?? true
  }

  /** The news waiting for her, by desk: for the tests and the lab. */
  pending(): ReadonlyMap<string, { event: ShioriEvent; at: number }> {
    return this.news
  }

  update(world: ShioriWorld, states: ReadonlyMap<string, AgentState>, now: number): void {
    for (const [deskId, state] of states) {
      const before = this.states.get(deskId)
      this.states.set(deskId, state)
      // What a desk already showed when she arrived is not news.
      const event = before === undefined ? null : eventOf(before, state)
      if (event) this.news.set(deskId, { event, at: now })
    }
    for (const [deskId, n] of this.news) if (!states.has(deskId) || !world.desks.get(deskId)?.length || now - n.at > STALE) this.news.delete(deskId)

    if (!this.plan) {
      // She comes in along the corridor, or wherever she can stand.
      const start = world.wander[world.wander.length - 1] ?? world.brain[0]
      if (!start) return
      this.plan = { mode: 'idle', spot: start, since: now, until: now + LINGER }
    }
    const plan = this.plan
    if (plan.mode === 'walking') {
      const where = along(plan.path, ((now - plan.start) * SHIORI_PACE) / 1000)
      // News comes first: off to it from wherever she is, unless she already brings some.
      if (plan.goal.kind !== 'desk' && this.next()) return this.go(world, where.at, now)
      if (where.done) this.arrive(plan.goal, plan.spot, now)
      return
    }
    if (plan.mode === 'talking') {
      if (now >= plan.until) this.go(world, plan.spot, now)
      return
    }
    if (now >= plan.until || this.next()) this.go(world, plan.spot, now)
  }

  /** Shiori as drawn now; undefined before the building gave her a place. */
  at(now: number): ShioriNow | undefined {
    const plan = this.plan
    if (!plan) return undefined
    if (plan.mode === 'walking') {
      const walked = ((now - plan.start) * SHIORI_PACE) / 1000
      const where = along(plan.path, walked)
      const heading = plan.goal.kind === 'desk' ? plan.goal.deskId : undefined
      if (where.done) return { ...plan.spot, x: plan.spot.x, y: plan.spot.y, facing: plan.spot.facing, step: 0, mode: 'walking', heading }
      return { ...where.at, facing: where.facing, step: Math.floor(walked / 4) % 4, mode: 'walking', heading }
    }
    return { x: plan.spot.x, y: plan.spot.y, facing: plan.spot.facing, step: 0, mode: plan.mode, talk: plan.talk }
  }

  /** The desk with the most urgent news, the oldest first among the same. */
  private next(): string | undefined {
    let best: [string, { event: ShioriEvent; at: number }] | undefined
    for (const entry of this.news) {
      const [, n] = entry
      if (!best || URGENCY[n.event] < URGENCY[best[1].event] || (URGENCY[n.event] === URGENCY[best[1].event] && n.at < best[1].at)) best = entry
    }
    return best?.[0]
  }

  /** Sets off from a point to the next place: news first, the brain now and then, else a stroll. */
  private go(world: ShioriWorld, from: Spot, now: number): void {
    const deskId = this.next()
    let goal: Goal
    let spot: StandSpot | undefined
    if (deskId) {
      goal = { kind: 'desk', deskId }
      const sides = world.desks.get(deskId) ?? []
      // The nearer side of the desk.
      spot = [...sides].sort((a, b) => Math.abs(a.x - from.x) + Math.abs(a.y - from.y) - (Math.abs(b.x - from.x) + Math.abs(b.y - from.y)))[0]
    } else {
      this.outings++
      const brain = this.outings % BRAIN_EVERY === 0 && world.brain.length > 0
      goal = brain ? { kind: 'brain' } : { kind: 'wander' }
      const choices = (brain ? world.brain : world.wander).filter((s) => s.x !== from.x || s.y !== from.y)
      spot = choices[Math.floor(chance(`shiori:${this.outings}`) * choices.length)] ?? world.brain[0]
    }
    if (!spot) return
    const route = this.motion ? findRoute(world, tileUnder(from.x, from.y), spot.tile) : null
    // Without motion, or without a way there, she simply appears at the place.
    if (!route) return this.arrive(goal, spot, now)
    const path = squared([from, ...route.map(frameOn), { x: spot.x, y: spot.y }])
    this.plan = { mode: 'walking', path, start: now, goal, spot }
    if (lengthOf(this.plan.path) === 0) this.arrive(goal, spot, now)
  }

  private arrive(goal: Goal, spot: StandSpot, now: number): void {
    if (goal.kind === 'desk') {
      const n = this.news.get(goal.deskId)
      this.news.delete(goal.deskId)
      if (n) {
        this.plan = { mode: 'talking', spot, since: now, until: now + TALK, talk: { deskId: goal.deskId, event: n.event } }
        return
      }
    }
    this.plan = goal.kind === 'brain' ? { mode: 'visiting', spot, since: now, until: now + VISIT } : { mode: 'idle', spot: { ...spot, facing: 'front' }, since: now, until: now + LINGER }
  }
}

/**
 * A path whose legs are all straight: a corner is added wherever two points differ both
 * ways, down or up first, except into the last point, which she reaches along the floor
 * first and then steps up or down to (the side of a desk).
 */
export function squared(points: readonly Spot[]): Spot[] {
  const path: Spot[] = []
  points.forEach((p, i) => {
    const last = path[path.length - 1]
    if (last && last.x !== p.x && last.y !== p.y) path.push(i === points.length - 1 ? { x: p.x, y: last.y } : { x: last.x, y: p.y })
    const now = path[path.length - 1]
    if (!now || now.x !== p.x || now.y !== p.y) path.push({ x: p.x, y: p.y })
  })
  return path
}
