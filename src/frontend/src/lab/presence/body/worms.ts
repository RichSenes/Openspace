import { onSphere, rng } from './shapes'

/**
 * Worms of light crawling on the swarm's surface: each one a head that wanders on the sphere
 * and a trail of where it has been. They are Shiori's thoughts: faster when she thinks.
 */
export interface Worm {
  head: [number, number, number]
  /** Unit direction of travel, tangent to the sphere at the head. */
  dir: [number, number, number]
  trail: [number, number, number][]
  seed: number
}

export interface Worms {
  list: Worm[]
  length: number
  /** Time since the last trail point, in seconds. */
  carry: number
  time: number
}

type V = [number, number, number]

const dot = (a: V, b: V) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
const cross = (a: V, b: V): V => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]
const scale = (a: V, k: number): V => [a[0] * k, a[1] * k, a[2] * k]
const add = (a: V, b: V): V => [a[0] + b[0], a[1] + b[1], a[2] + b[2]]
export const norm = (a: V): V => {
  const l = Math.hypot(a[0], a[1], a[2]) || 1
  return [a[0] / l, a[1] / l, a[2] / l]
}

/** The part of v tangent to the sphere at p, normalised. */
function tangent(v: V, p: V): V {
  return norm(add(v, scale(p, -dot(v, p))))
}

export function makeWorms(count = 9, length = 26, seed = 5): Worms {
  const r = rng(seed)
  const list: Worm[] = []
  for (let i = 0; i < count; i++) {
    const head = onSphere(r)
    const dir = tangent(onSphere(r), head)
    list.push({ head, dir, trail: Array.from({ length }, () => head), seed: r() })
  }
  return { list, length, carry: 0, time: 0 }
}

/** Trail points per second: how finely a trail is drawn. */
const STEP = 1 / 40

/**
 * Moves every worm along the sphere for dt seconds at `speed` (radians per second at 1).
 * The heading turns smoothly with a per-worm wave, so paths curl and never look random.
 */
export function stepWorms(w: Worms, dt: number, speed: number): Worms {
  let carry = w.carry + dt
  let time = w.time
  const list = w.list.map((x) => ({ ...x, trail: [...x.trail] }))
  while (carry >= STEP) {
    carry -= STEP
    time += STEP
    for (const worm of list) {
      const turn = Math.sin(time * (0.7 + worm.seed) + worm.seed * 40) * 2.2 + Math.sin(time * 2.9 + worm.seed * 9) * 0.8
      const side = cross(worm.head, worm.dir)
      const a = turn * STEP
      worm.dir = norm(add(scale(worm.dir, Math.cos(a)), scale(side, Math.sin(a))))
      const step = speed * STEP
      worm.head = norm(add(worm.head, scale(worm.dir, step)))
      worm.dir = tangent(worm.dir, worm.head)
      worm.trail.unshift(worm.head)
      worm.trail.length = w.length
    }
  }
  return { list, length: w.length, carry, time }
}

/** Writes every trail point into a flat xyz array (heads first in each worm). */
export function wormPositions(w: Worms, out: Float32Array): Float32Array {
  let k = 0
  for (const worm of w.list)
    for (const p of worm.trail) {
      out[k++] = p[0]
      out[k++] = p[1]
      out[k++] = p[2]
    }
  return out
}
