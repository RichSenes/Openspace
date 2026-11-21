import { describe, expect, it } from 'vitest'
import { buildSkin, fibonacciSphere, KIND, rng, type SkinId } from './shapes'

const SKINS: SkinId[] = ['swarm', 'medusa']

describe('skins', () => {
  it.each(SKINS)('%s has matching attribute arrays and both eyes', (id) => {
    const s = buildSkin(id)
    expect(s.count).toBeGreaterThan(500)
    expect(s.pos.length).toBe(s.count * 3)
    expect(s.data.length).toBe(s.count * 4)
    expect(s.extra.length).toBe(s.count * 2)
    const kinds = Array.from({ length: s.count }, (_, i) => s.data[i * 4])
    const left = kinds.filter((k) => k === KIND.leftEye).length
    const right = kinds.filter((k) => k === KIND.rightEye).length
    expect(left).toBeGreaterThan(60)
    expect(left).toBe(right)
  })

  it.each(SKINS)('%s is the same on every build', (id) => {
    expect(buildSkin(id).pos).toEqual(buildSkin(id).pos)
  })

  it('puts the swarm on the unit sphere', () => {
    const s = buildSkin('swarm')
    for (let i = 0; i < 2600; i += 97) expect(Math.hypot(s.pos[i * 3], s.pos[i * 3 + 1], s.pos[i * 3 + 2])).toBeCloseTo(1, 5)
  })

  it('hangs medusa tentacles below the bell', () => {
    const s = buildSkin('medusa')
    const strands = Array.from({ length: s.count }, (_, i) => i).filter((i) => s.data[i * 4] === KIND.strand)
    expect(strands.length).toBeGreaterThan(1000)
    for (const i of strands) expect(s.data[i * 4 + 2]).toBeGreaterThanOrEqual(0)
  })

  it('keeps eye discs and smile arcs inside a unit square', () => {
    const s = buildSkin('swarm')
    for (let i = 0; i < s.count; i++) {
      if (s.data[i * 4] < KIND.leftEye) continue
      expect(Math.hypot(s.data[i * 4 + 2], s.data[i * 4 + 3])).toBeLessThanOrEqual(1.0001)
      expect(Math.abs(s.extra[i * 2])).toBeLessThanOrEqual(1)
      expect(Math.abs(s.extra[i * 2 + 1])).toBeLessThanOrEqual(1)
    }
  })
})

describe('the portrait', () => {
  it('has no points: it is drawn, not built', () => {
    expect(buildSkin('portrait').count).toBe(0)
  })
})

describe('helpers', () => {
  it('spreads golden-spiral points over the whole sphere', () => {
    const p = fibonacciSphere(400)
    expect(Math.max(...p.map((v) => v[1]))).toBeGreaterThan(0.99)
    expect(Math.min(...p.map((v) => v[1]))).toBeLessThan(-0.99)
  })

  it('draws numbers in 0..1 and repeats from the same seed', () => {
    const a = rng(3)
    const b = rng(3)
    for (let i = 0; i < 50; i++) {
      const x = a()
      expect(x).toBeGreaterThanOrEqual(0)
      expect(x).toBeLessThan(1)
      expect(b()).toBe(x)
    }
  })
})
