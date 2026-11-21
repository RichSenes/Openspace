import { describe, expect, it } from 'vitest'
import { makeWorms, stepWorms, wormPositions } from './worms'

describe('worms of light', () => {
  it('keep crawling on the sphere with trails of fixed length', () => {
    let w = makeWorms(5, 12)
    for (let i = 0; i < 300; i++) w = stepWorms(w, 1 / 60, 1.2)
    for (const worm of w.list) {
      expect(worm.trail).toHaveLength(12)
      expect(Math.hypot(...worm.head)).toBeCloseTo(1, 6)
      expect(Math.abs(worm.dir[0] * worm.head[0] + worm.dir[1] * worm.head[1] + worm.dir[2] * worm.head[2])).toBeLessThan(1e-6)
    }
  })

  it('travel further when they go faster', () => {
    const start = makeWorms(1, 4)
    const slow = stepWorms(start, 0.5, 0.2).list[0].head
    const fast = stepWorms(start, 0.5, 1.5).list[0].head
    const from = start.list[0].head
    const dist = (a: number[]) => Math.hypot(a[0] - from[0], a[1] - from[1], a[2] - from[2])
    expect(dist(fast)).toBeGreaterThan(dist(slow))
  })

  it('stand still at speed zero', () => {
    const start = makeWorms(2, 4)
    const after = stepWorms(start, 1, 0)
    expect(after.list[0].head[0]).toBeCloseTo(start.list[0].head[0], 9)
  })

  it('write every trail point into a flat array', () => {
    const w = makeWorms(3, 5)
    const out = wormPositions(w, new Float32Array(3 * 5 * 3))
    expect(out[0]).toBeCloseTo(w.list[0].head[0], 6)
    expect(out[out.length - 1]).toBeCloseTo(w.list[2].trail[4][2], 6)
  })
})
