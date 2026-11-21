import { describe, expect, it } from 'vitest'
import { buildSkin, bustSection, fibonacciSphere, KIND, noseAt, rng, type SkinId } from './shapes'

const SKINS: SkinId[] = ['swarm', 'medusa', 'scanline']

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

describe('the bust', () => {
  it('has a skull, a narrower neck and shoulders that slope out wide', () => {
    const skull = bustSection(0.5)!
    const neck = bustSection(-0.05)!
    const slope = bustSection(-0.3)!
    const shoulders = bustSection(-0.6)!
    expect(neck.rx).toBeLessThan(skull.rx)
    expect(slope.rx).toBeGreaterThan(neck.rx)
    expect(shoulders.rx).toBeGreaterThan(slope.rx)
    expect(shoulders.rx).toBeGreaterThan(skull.rx * 2)
    expect(bustSection(0.9)).toBeNull()
  })

  it('has a face in front: a nose with its tip at mid height, a chin narrower than the skull', () => {
    expect(noseAt(0.3)).toBeGreaterThan(noseAt(0.4))
    expect(noseAt(0.5)).toBe(0)
    expect(bustSection(0.08)!.rx).toBeLessThan(bustSection(0.5)!.rx * 0.6)
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
