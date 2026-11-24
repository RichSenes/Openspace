import { describe, expect, it } from 'vitest'
import { apply, bodyRotation, pointSize, project, RIGS, rotation } from './rig'

describe('rotation', () => {
  it('keeps lengths and turns +z toward +x with yaw', () => {
    const m = rotation(Math.PI / 2, 0)
    const v = apply(m, [0, 0, 1])
    expect(v[0]).toBeCloseTo(1, 6)
    const r = rotation(0.7, -0.4)
    expect(Math.hypot(...apply(r, [0.3, -0.5, 0.8]))).toBeCloseTo(Math.hypot(0.3, -0.5, 0.8), 6)
  })

  it('lets a creature keep its eyes on you while its body turns', () => {
    const swarm = bodyRotation('swarm', 1, 1, [0.5, 0], 0)
    expect(swarm.eyeRot).not.toEqual(swarm.rot)
  })
})

describe('projection', () => {
  it('puts the body centre at the focus and scales with it', () => {
    const view = { w: 1000, h: 800, scale: 0.5, offset: [0, 0.2] as [number, number] }
    const c = project([0, 0, 0], rotation(0, 0), { bodyScale: 1, lift: 0 }, view)
    expect(c.x).toBeCloseTo(500, 4)
    expect(c.y).toBeCloseTo(320, 4)
    const right = project([1, 0, 0], rotation(0, 0), { bodyScale: 1, lift: 0 }, view)
    expect(right.x).toBeGreaterThan(c.x)
  })

  it('names three parts on every body', () => {
    for (const rig of Object.values(RIGS)) expect(rig.parts).toHaveLength(3)
  })
})

describe('pointSize', () => {
  it('grows with her size on screen and stays fully lit when large', () => {
    const big = pointSize(900, 0.6, true)
    expect(big.size).toBeCloseTo(3.6, 6)
    expect(big.dim).toBe(1)
    expect(pointSize(1800, 0.6, true).size).toBeCloseTo(7.2, 6)
  })

  it('never goes under one pixel, and dims instead when she is small', () => {
    const small = pointSize(96, 0.7, true)
    expect(small.size).toBe(1)
    expect(small.dim).toBeGreaterThan(0.1)
    expect(small.dim).toBeLessThan(0.6)
    expect(pointSize(48, 0.7, true).dim).toBeLessThan(small.dim)
  })
})
