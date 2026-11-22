import type { Graph, GraphLink, GraphNote } from './types'
import { topFolder } from './wiki'

export interface GraphFilter {
  /** A first-level folder of the vault; "" is the notes at its root; undefined is every folder. */
  folder?: string
  kind?: string
  author?: string
}

/** The notes that pass the filter, and the links between two of them. */
export function filterGraph(graph: Graph, filter: GraphFilter): Graph {
  const notes = graph.notes.filter(
    (n) =>
      (filter.folder === undefined || topFolder(n.path) === filter.folder) &&
      (!filter.kind || n.kind === filter.kind) &&
      (!filter.author || n.author === filter.author),
  )
  const kept = new Set(notes.map((n) => n.path))
  return { notes, links: graph.links.filter((l) => kept.has(l.from) && kept.has(l.to)) }
}

/** The first-level folders of the notes, sorted, "" (the root) first when there is one. */
export function foldersOfGraph(notes: GraphNote[]): string[] {
  return [...new Set(notes.map((n) => topFolder(n.path)))].sort((a, b) => (a === '' ? -1 : b === '' ? 1 : a.localeCompare(b)))
}

export function authorsOf(notes: GraphNote[]): string[] {
  return [...new Set(notes.map((n) => n.author).filter((a): a is string => !!a))].sort()
}

export interface Neighbours {
  out: string[]
  in: string[]
}

/** For each note, the notes it links to and those that link to it. */
export function neighbours(links: GraphLink[]): Map<string, Neighbours> {
  const map = new Map<string, Neighbours>()
  const of = (path: string) => {
    let n = map.get(path)
    if (!n) map.set(path, (n = { out: [], in: [] }))
    return n
  }
  for (const link of links) {
    of(link.from).out.push(link.to)
    of(link.to).in.push(link.from)
  }
  return map
}

/** The number of links that touch each note. */
export function degrees(links: GraphLink[]): Map<string, number> {
  const map = new Map<string, number>()
  for (const l of links) {
    map.set(l.from, (map.get(l.from) ?? 0) + 1)
    map.set(l.to, (map.get(l.to) ?? 0) + 1)
  }
  return map
}

/* ---------------------------------------------------------------------------------------------
 * A small force simulation: notes push each other apart, links pull their ends together, and
 * every note is drawn gently towards its folder's place on a ring, so folders gather. It starts
 * from a sunflower spiral and uses no randomness: the same notes always settle the same way.
 * ------------------------------------------------------------------------------------------- */

export interface Simulation {
  paths: string[]
  x: Float64Array
  y: Float64Array
  vx: Float64Array
  vy: Float64Array
  /** Link ends as indexes into the arrays above. */
  source: Int32Array
  target: Int32Array
  /** Where each note's folder gathers. */
  anchorX: Float64Array
  anchorY: Float64Array
  /** Heat: forces scale with it and it cools at every step; below MIN_ALPHA the layout is done. */
  alpha: number
}

export const MIN_ALPHA = 0.004
const ALPHA_DECAY = 0.028
const VELOCITY_DECAY = 0.6
const REPULSION = 900
const LINK_DISTANCE = 42
const LINK_STRENGTH = 0.08
const ANCHOR_STRENGTH = 0.02
const GOLDEN = Math.PI * (3 - Math.sqrt(5))

export function createSimulation(notes: GraphNote[], links: GraphLink[]): Simulation {
  const paths = notes.map((n) => n.path)
  const index = new Map(paths.map((p, i) => [p, i]))
  const count = paths.length
  const x = new Float64Array(count)
  const y = new Float64Array(count)
  for (let i = 0; i < count; i++) {
    const r = 12 * Math.sqrt(i + 0.5)
    x[i] = r * Math.cos(i * GOLDEN)
    y[i] = r * Math.sin(i * GOLDEN)
  }
  const folders = foldersOfGraph(notes)
  const ring = 40 * Math.sqrt(count) * (folders.length > 1 ? 1 : 0)
  const anchorX = new Float64Array(count)
  const anchorY = new Float64Array(count)
  notes.forEach((n, i) => {
    const f = folders.indexOf(topFolder(n.path))
    const angle = (f / Math.max(1, folders.length)) * Math.PI * 2
    anchorX[i] = ring * 0.5 * Math.cos(angle)
    anchorY[i] = ring * 0.5 * Math.sin(angle)
  })
  const kept = links.filter((l) => index.has(l.from) && index.has(l.to))
  return {
    paths,
    x,
    y,
    vx: new Float64Array(count),
    vy: new Float64Array(count),
    source: Int32Array.from(kept, (l) => index.get(l.from)!),
    target: Int32Array.from(kept, (l) => index.get(l.to)!),
    anchorX,
    anchorY,
    alpha: 1,
  }
}

/** One tick of the simulation, in place. Returns false once it has cooled and nothing moves. */
export function step(sim: Simulation): boolean {
  if (sim.alpha < MIN_ALPHA) return false
  const { x, y, vx, vy, source, target, anchorX, anchorY, alpha } = sim
  const count = x.length

  // Every pair pushes apart, more when close; a floor on the distance keeps it finite.
  for (let i = 0; i < count; i++) {
    for (let j = i + 1; j < count; j++) {
      let dx = x[j] - x[i]
      let dy = y[j] - y[i]
      let d2 = dx * dx + dy * dy
      if (d2 < 1) {
        // Two notes on the same spot: part them along a fixed direction.
        dx = 1
        dy = 0
        d2 = 1
      }
      if (d2 > 90_000) continue
      const f = (REPULSION * alpha) / d2
      vx[i] -= dx * f
      vy[i] -= dy * f
      vx[j] += dx * f
      vy[j] += dy * f
    }
  }

  // Links pull towards their rest length.
  for (let k = 0; k < source.length; k++) {
    const s = source[k]
    const t = target[k]
    const dx = x[t] - x[s]
    const dy = y[t] - y[s]
    const d = Math.sqrt(dx * dx + dy * dy) || 1
    const f = ((d - LINK_DISTANCE) / d) * LINK_STRENGTH * alpha
    vx[s] += dx * f
    vy[s] += dy * f
    vx[t] -= dx * f
    vy[t] -= dy * f
  }

  for (let i = 0; i < count; i++) {
    vx[i] += (anchorX[i] - x[i]) * ANCHOR_STRENGTH * alpha
    vy[i] += (anchorY[i] - y[i]) * ANCHOR_STRENGTH * alpha
    vx[i] *= VELOCITY_DECAY
    vy[i] *= VELOCITY_DECAY
    x[i] += vx[i]
    y[i] += vy[i]
  }
  sim.alpha *= 1 - ALPHA_DECAY
  return true
}

/** Runs the simulation until it cools (or for at most `limit` steps): the layout without motion. */
export function settle(sim: Simulation, limit = 400): Simulation {
  for (let i = 0; i < limit && step(sim); i++) {
    // keep stepping
  }
  return sim
}

/** The box around every note: where the camera starts. */
export function bounds(xs: ArrayLike<number>, ys: ArrayLike<number>): { minX: number; minY: number; maxX: number; maxY: number } {
  let minX = Infinity
  let minY = Infinity
  let maxX = -Infinity
  let maxY = -Infinity
  for (let i = 0; i < xs.length; i++) {
    minX = Math.min(minX, xs[i])
    maxX = Math.max(maxX, xs[i])
    minY = Math.min(minY, ys[i])
    maxY = Math.max(maxY, ys[i])
  }
  return xs.length ? { minX, minY, maxX, maxY } : { minX: -1, minY: -1, maxX: 1, maxY: 1 }
}

/* ---------------------------------------------------------------------------------------------
 * The city: each first-level folder is a block of small buildings, one per note, in rows; the
 * blocks stand side by side and wrap into streets. Taller buildings have more links.
 * ------------------------------------------------------------------------------------------- */

export const LOT = 14
/** The height of a floor, in world units. */
export const FLOOR = 5
/** Room in a block above its first row for the tallest building. */
const HEAD = 7 * FLOOR + 6
const STREET = 26

export interface CityBlock {
  folder: string
  x: number
  y: number
  width: number
  height: number
}

export interface City {
  /** The middle of each building's foot, in the order of the notes given. */
  x: Float64Array
  y: Float64Array
  /** Floors, 1 to 7. */
  floors: Uint8Array
  blocks: CityBlock[]
}

export function cityLayout(notes: GraphNote[], links: GraphLink[]): City {
  const degree = degrees(links)
  const folders = foldersOfGraph(notes)
  const x = new Float64Array(notes.length)
  const y = new Float64Array(notes.length)
  const floors = Uint8Array.from(notes, (n) => Math.min(7, 1 + (degree.get(n.path) ?? 0)))
  const blocks: CityBlock[] = []
  const perRow = Math.max(1, Math.ceil(Math.sqrt(folders.length)))
  let cursorX = 0
  let cursorY = 0
  let rowHeight = 0
  folders.forEach((folder, f) => {
    const inside = notes.map((n, i) => [n, i] as const).filter(([n]) => topFolder(n.path) === folder)
    const cols = Math.max(2, Math.ceil(Math.sqrt(inside.length)))
    const rows = Math.ceil(inside.length / cols)
    const width = cols * LOT
    const height = HEAD + (rows - 1) * LOT + 6
    if (f > 0 && f % perRow === 0) {
      cursorX = 0
      cursorY += rowHeight + STREET
      rowHeight = 0
    }
    inside.forEach(([, i], k) => {
      x[i] = cursorX + (k % cols) * LOT + LOT / 2
      y[i] = cursorY + HEAD + Math.floor(k / cols) * LOT
    })
    blocks.push({ folder, x: cursorX, y: cursorY, width, height })
    cursorX += width + STREET
    rowHeight = Math.max(rowHeight, height)
  })
  return { x, y, floors, blocks }
}

/** A small stable number from a text, for the windows that are lit and the stars' places. */
export function hash(text: string): number {
  let h = 2166136261
  for (let i = 0; i < text.length; i++) {
    h ^= text.charCodeAt(i)
    h = Math.imul(h, 16777619)
  }
  return h >>> 0
}

/** The note nearest to a point within `radius`, or -1. */
export function nearest(xs: ArrayLike<number>, ys: ArrayLike<number>, px: number, py: number, radius: number): number {
  let best = -1
  let bestD = radius * radius
  for (let i = 0; i < xs.length; i++) {
    const dx = xs[i] - px
    const dy = ys[i] - py
    const d = dx * dx + dy * dy
    if (d <= bestD) {
      bestD = d
      best = i
    }
  }
  return best
}

/** Indexes grouped by the line they stand on, from the back (smallest y) to the front. */
export function rowsOf(ys: ArrayLike<number>): number[][] {
  const rows = new Map<number, number[]>()
  for (let i = 0; i < ys.length; i++) {
    const row = rows.get(ys[i])
    if (row) row.push(i)
    else rows.set(ys[i], [i])
  }
  return [...rows.entries()].sort((a, b) => a[0] - b[0]).map(([, row]) => row)
}
