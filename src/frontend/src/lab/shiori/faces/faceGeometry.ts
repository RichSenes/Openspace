/** Pure drawing maths for the three faces: pixel eyes, the ink web. No DOM here. */

/** 4×4 ordered dither thresholds, 0..1. */
export const BAYER4 = [
  [0, 8, 2, 10],
  [12, 4, 14, 6],
  [3, 11, 1, 9],
  [15, 7, 13, 5],
].map((row) => row.map((v) => (v + 0.5) / 16))

export const EYE_W = 8
export const EYE_H = 10

export type EyeShape = 'open' | 'happy'

/**
 * The cells of one pixel eye, as rows of 0/1, EYE_W wide. `open` 0..1 lowers the lid from the top
 * (0 is a closed line); `slant` -1..1 lowers one side of the lid more than the other.
 */
export function eyeMask(open: number, shape: EyeShape = 'open', slant = 0, tall = 0): number[][] {
  const h = EYE_H + tall
  const rows: number[][] = []
  if (shape === 'happy') {
    // An upturned arc, like ^, three rows high.
    for (let y = 0; y < h; y++) rows.push(new Array<number>(EYE_W).fill(0))
    const mid = Math.floor(h / 2)
    const arc = [
      [0, 0, 1, 1, 1, 1, 0, 0],
      [0, 1, 1, 0, 0, 1, 1, 0],
      [1, 1, 0, 0, 0, 0, 1, 1],
    ]
    arc.forEach((r, k) => (rows[mid - 1 + k] = [...r]))
    return rows
  }
  const o = Math.max(0, Math.min(1, open))
  for (let y = 0; y < h; y++) {
    const row: number[] = []
    for (let x = 0; x < EYE_W; x++) {
      // Rounded corners: the four corner cells are never filled.
      const corner = (y === 0 || y === h - 1) && (x === 0 || x === EYE_W - 1)
      const lid = Math.round((1 - o) * (h - 1) + slant * ((x / (EYE_W - 1)) * 2 - 1) * 2)
      row.push(!corner && y >= lid ? 1 : 0)
    }
    rows.push(row)
  }
  // A closed eye is one line, never nothing.
  if (!rows.some((r) => r.includes(1))) rows[h - 1] = rows[h - 1].map((_, x) => (x > 0 && x < EYE_W - 1 ? 1 : 0))
  return rows
}

/** Small deterministic random numbers, so the web is the same drawing every time. */
export function seeded(seed: number): () => number {
  let s = seed >>> 0
  return () => {
    s = (s + 0x6d2b79f5) >>> 0
    let t = s
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

export interface WebNode {
  x: number
  y: number
  r: number
  /** 0..1, phase of its own breathing. */
  phase: number
}

export interface WebEdge {
  a: number
  b: number
  /** Sideways bend of the stroke, in units of its length: the hand-drawn wobble. */
  bend: number
}

export interface Web {
  nodes: WebNode[]
  edges: WebEdge[]
}

/** A neural web in a unit disc around (0,0): a hub, rings of nodes, each joined to its nearest. */
export function makeWeb(count = 22, seed = 7): Web {
  const rand = seeded(seed)
  const nodes: WebNode[] = [{ x: 0, y: 0, r: 4, phase: 0 }]
  let guard = 0
  while (nodes.length < count && guard++ < 5000) {
    const a = rand() * Math.PI * 2
    const d = 0.25 + Math.sqrt(rand()) * 0.72
    const x = Math.cos(a) * d
    const y = Math.sin(a) * d
    if (nodes.every((n) => Math.hypot(n.x - x, n.y - y) > 0.24)) nodes.push({ x, y, r: 1.6 + rand() * 1.8, phase: rand() })
  }
  const edges: WebEdge[] = []
  const has = (a: number, b: number) => edges.some((e) => (e.a === a && e.b === b) || (e.a === b && e.b === a))
  nodes.forEach((n, i) => {
    const near = nodes
      .map((m, j) => ({ j, d: Math.hypot(m.x - n.x, m.y - n.y) }))
      .filter((o) => o.j !== i)
      .sort((p, q) => p.d - q.d)
      .slice(0, i === 0 ? 5 : 2)
    for (const o of near) if (!has(i, o.j)) edges.push({ a: i, b: o.j, bend: (rand() - 0.5) * 0.35 })
  })
  return { nodes, edges }
}

/** The control point of an edge's quadratic curve, bent sideways by `bend`. */
export function bendPoint(ax: number, ay: number, bx: number, by: number, bend: number): [number, number] {
  const mx = (ax + bx) / 2
  const my = (ay + by) / 2
  return [mx - (by - ay) * bend, my + (bx - ax) * bend]
}

/** A point at t (0..1) along a quadratic curve. */
export function quadAt(ax: number, ay: number, cx: number, cy: number, bx: number, by: number, t: number): [number, number] {
  const u = 1 - t
  return [u * u * ax + 2 * u * t * cx + t * t * bx, u * u * ay + 2 * u * t * cy + t * t * by]
}
