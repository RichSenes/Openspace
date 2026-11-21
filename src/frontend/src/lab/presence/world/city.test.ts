import { describe, expect, it } from 'vitest'
import { makeCity } from './city'

describe('the city', () => {
  it('is the same city for the same size', () => {
    expect(makeCity(300, 200)).toEqual(makeCity(300, 200))
  })

  it('fills the width with buildings below the sky', () => {
    const c = makeCity(320, 240)
    expect(c.horizon).toBeGreaterThan(240 * 0.6)
    expect(c.horizon).toBeLessThan(240 * 0.9)
    for (const row of [c.far, c.mid, c.near]) {
      expect(row[0].x).toBeLessThanOrEqual(0)
      const last = row[row.length - 1]
      expect(last.x + last.w).toBeGreaterThanOrEqual(320 - 3)
      for (const b of row) expect(b.h).toBeGreaterThan(0)
    }
  })

  it('keeps every lit window inside its building', () => {
    const c = makeCity(400, 300)
    for (const b of [...c.far, ...c.mid, ...c.near])
      for (const w of b.windows) {
        expect(w.x).toBeLessThan(b.w)
        expect(w.y).toBeLessThan(b.h)
      }
  })
})
