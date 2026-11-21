import { describe, expect, it } from 'vitest'
import { apply, bodyRotation, project, RIGS, rotation } from './rig'

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
