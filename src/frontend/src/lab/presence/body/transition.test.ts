import { describe, expect, it } from 'vitest'
import { changeAt, DURATION } from './transition'

describe('changes of body and mode', () => {
  it('show nothing when nothing changes', () => {
    expect(changeAt(null, 3, false)).toEqual({ ascii: 0, glitch: 0, swapped: true, done: true })
  })

  it('dissolve fully into characters for a new body and swap it at the peak', () => {
    const c = { kind: 'skin' as const, start: 10 }
    const early = changeAt(c, 10 + DURATION.skin * 0.2, false)
    const peak = changeAt(c, 10 + DURATION.skin * 0.4, false)
    expect(early.swapped).toBe(false)
    expect(peak.ascii).toBeGreaterThan(0.95)
    expect(peak.glitch).toBeGreaterThan(0.5)
    expect(changeAt(c, 10 + DURATION.skin * 0.5, false).swapped).toBe(true)
    expect(changeAt(c, 10 + DURATION.skin + 0.001, false).done).toBe(true)
  })

  it('only half dissolve for a mode switch', () => {
    const c = { kind: 'mode' as const, start: 0 }
    const peak = Math.max(...[0.3, 0.4, 0.5].map((k) => changeAt(c, DURATION.mode * k, false).ascii))
    expect(peak).toBeLessThan(0.6)
    expect(peak).toBeGreaterThan(0.4)
  })

  it('never glitch with calm motion, and end sooner', () => {
    const c = { kind: 'skin' as const, start: 0 }
    expect(changeAt(c, DURATION.skin * 0.4, true).glitch).toBe(0)
    expect(changeAt(c, DURATION.skin * 0.7, true).done).toBe(true)
  })
})
