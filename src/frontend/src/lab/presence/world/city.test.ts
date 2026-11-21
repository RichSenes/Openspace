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

import { hourOf, LIGHTS, HOURS, rainAmount, SKIES, stormFlash } from './city'

describe('hours and weather', () => {
  it('reads the hour of the world from the clock', () => {
    const at = (h: number, m = 0) => new Date(2026, 10, 21, h, m)
    expect(hourOf(at(6))).toBe('dawn')
    expect(hourOf(at(12))).toBe('day')
    expect(hourOf(at(18))).toBe('dusk')
    expect(hourOf(at(23))).toBe('night')
    expect(hourOf(at(3))).toBe('night')
  })

  it('lights more windows by night than by day', () => {
    expect(LIGHTS.night.lit).toBeGreaterThan(LIGHTS.dusk.lit)
    expect(LIGHTS.dusk.lit).toBeGreaterThan(LIGHTS.day.lit)
    for (const h of HOURS) expect(LIGHTS[h].sky.length).toBeGreaterThanOrEqual(6)
  })

  it('rains harder in a storm and not at all when clear or snowing', () => {
    expect(rainAmount('Storm')).toBeGreaterThan(rainAmount('Rain'))
    expect(rainAmount('Rain')).toBeGreaterThan(rainAmount('Drizzle'))
    expect(rainAmount('Clear')).toBe(0)
    expect(rainAmount('Snow')).toBe(0)
    expect(SKIES).toHaveLength(7)
  })

  it('flashes twice in quick succession, then waits', () => {
    expect(stormFlash(7.02)).toBe(1)
    expect(stormFlash(7.22)).toBeGreaterThan(0)
    expect(stormFlash(10)).toBe(0)
  })

  it('hangs the sign on a block inside the city', () => {
    const c = makeCity(400, 300)
    expect(c.sign.x).toBeGreaterThan(400 * 0.7)
    expect(c.sign.x + 10).toBeLessThanOrEqual(400)
    expect(c.sign.y).toBeLessThan(c.horizon)
  })
})
