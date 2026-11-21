import { describe, expect, it } from 'vitest'
import { along, makeMachine, PACE } from './machine'
import { bayerAt, fbm, valueNoise } from './pixels'
import { makeSpace, sunAt } from './space'
import { KYOTO_LIGHTS, makeKyoto, skyLanternAt } from './kyoto'
import { makeLibrary, towerShift } from './library'
import { HOURS, SKIES } from './types'
import { WORLD_ORDER, WORLDS } from './worlds'

describe('worlds', () => {
  it.each(WORLD_ORDER)('%s names every hour and sky, and lends Shiori up to 8 colours', (id) => {
    const w = WORLDS[id]
    for (const h of HOURS) expect(w.hours[h]).toBeTruthy()
    for (const s of SKIES) expect(w.skies[s]).toBeTruthy()
    expect(w.palette.length).toBeLessThanOrEqual(8)
    expect(w.palette[0]).toEqual([0, 0, 0])
  })

  it('turns rain into meteors in space and into code inside the machine', () => {
    expect(WORLDS.space.skies.Rain).toBe('Meteor shower')
    expect(WORLDS.machine.skies.Rain).toBe('Code rain')
    expect(WORLDS.machine.hours.night).toBe('Asleep')
  })
})

describe('space', () => {
  it('hides its star on the night side and moves it with the hour', () => {
    const s = makeSpace(400, 300)
    expect(sunAt(s, 'night')).toBeNull()
    const dawn = sunAt(s, 'dawn')!
    const day = sunAt(s, 'day')!
    expect(day.y).toBeLessThan(dawn.y)
  })

  it('keeps its distant planets small and out of the middle', () => {
    const s = makeSpace(400, 300)
    expect(s.planets.length).toBeGreaterThan(3)
    for (const p of s.planets) {
      expect(p.r).toBeLessThanOrEqual(8)
      expect(Math.abs(p.x - 200) >= 80 || p.y >= s.sill * 0.75).toBe(true)
    }
    expect(s.giant.r).toBeLessThanOrEqual(84)
  })

  it('is the same sky for the same size', () => {
    expect(makeSpace(300, 200).stars).toEqual(makeSpace(300, 200).stars)
  })
})

describe('the machine', () => {
  it('runs its traces at right angles inside the board', () => {
    const m = makeMachine(400, 300)
    for (const t of m.traces) {
      for (let i = 1; i < t.points.length; i++) {
        const [ax, ay] = t.points[i - 1]
        const [bx, by] = t.points[i]
        expect(ax === bx || ay === by).toBe(true)
        expect(bx).toBeGreaterThanOrEqual(0)
        expect(bx).toBeLessThan(400)
        expect(by).toBeLessThan(m.floor)
      }
    }
  })

  it('finds any point along a trace, wrapping around its length', () => {
    const m = makeMachine(400, 300)
    const t = m.traces.find((x) => x.length > 0)!
    expect(along(t, 0)).toEqual(t.points[0])
    expect(along(t, t.length)).toEqual(along(t, 0))
  })

  it('keeps its chips off the middle, where Shiori stands', () => {
    const m = makeMachine(400, 300)
    for (const c of m.chips) expect(c.x + c.w <= 400 * 0.3 || c.x >= 400 * 0.7 || c.y >= m.floor * 0.8).toBe(true)
  })

  it('is dense: many traces, a fine layer under them and small parts everywhere', () => {
    const m = makeMachine(400, 300)
    expect(m.traces.length).toBeGreaterThan(80)
    expect(m.fine.length).toBeGreaterThan(m.traces.length)
    expect(m.parts.length).toBeGreaterThan(300)
  })

  it('is busier by day than asleep at night', () => {
    expect(PACE.day.signals).toBeGreaterThan(PACE.night.signals)
    expect(PACE.day.speed).toBeGreaterThan(PACE.night.speed)
  })
})

describe('pixel tools', () => {
  it('dithers with thresholds strictly between 0 and 1', () => {
    for (let y = 0; y < 4; y++) for (let x = 0; x < 4; x++) {
      expect(bayerAt(x, y)).toBeGreaterThan(0)
      expect(bayerAt(x, y)).toBeLessThan(1)
    }
  })

  it('makes smooth, repeatable noise in 0..1', () => {
    const a = valueNoise(5)
    const b = valueNoise(5)
    for (let i = 0; i < 40; i++) {
      const v = fbm(a, i * 0.37, i * 0.21)
      expect(v).toBeGreaterThanOrEqual(0)
      expect(v).toBeLessThanOrEqual(1)
      expect(fbm(b, i * 0.37, i * 0.21)).toBe(v)
    }
    expect(Math.abs(a(1.5, 1.5) - a(1.51, 1.5))).toBeLessThan(0.05)
  })
})

describe('Kyoto', () => {
  it('is seen from far: a town of many small houses below the ridges', () => {
    const k = makeKyoto(400, 300)
    const all = k.terraces.flatMap((t) => t.houses)
    expect(all.length).toBeGreaterThan(80)
    for (const hs of all) expect(hs.h).toBeLessThanOrEqual(16)
    for (let i = 1; i < k.terraces.length; i++) expect(k.terraces[i].base).toBeGreaterThan(k.terraces[i - 1].base)
  })

  it('has pagodas, a path of gates and a bamboo grove, all small', () => {
    const k = makeKyoto(400, 300)
    expect(k.pagodas.length).toBeGreaterThanOrEqual(3)
    expect(k.gates.length).toBeGreaterThan(8)
    expect(k.grove.x1 - k.grove.x0).toBeLessThan(400 * 0.15)
  })

  it('lets sky lanterns rise from the town and start again', () => {
    const k = makeKyoto(400, 300)
    const l = k.skyLanterns[0]
    expect(skyLanternAt(l, k, 2).y).toBeLessThan(skyLanternAt(l, k, 1).y)
    for (let t = 0; t < 200; t += 3) expect(skyLanternAt(l, k, t).y).toBeLessThanOrEqual(k.horizon + 30)
  })

  it('lights its lanterns after dark only', () => {
    expect(KYOTO_LIGHTS.day.lantern).toBe(0)
    expect(KYOTO_LIGHTS.night.lantern).toBe(1)
  })
})

describe('the endless library', () => {
  it('builds towers in three depths, some hanging from above, none of the near ones behind Shiori', () => {
    const lib = makeLibrary(400, 300)
    for (const layer of [0, 1, 2]) expect(lib.towers.some((t) => t.layer === layer)).toBe(true)
    expect(lib.towers.some((t) => t.hanging)).toBe(true)
    expect(lib.towers.some((t) => !t.hanging)).toBe(true)
    for (const t of lib.towers.filter((x) => x.layer > 0)) expect(t.x + t.w < 400 * 0.32 || t.x > 400 * 0.68).toBe(true)
  })

  it('lets a tower glide to a new height now and then, and rest in between', () => {
    const lib = makeLibrary(400, 300)
    const tw = lib.towers[0]
    const seen = new Set<number>()
    let still = 0
    for (let t = 0; t < 120; t += 0.5) {
      const a = towerShift(tw, t, false)
      seen.add(a)
      if (a === towerShift(tw, t + 0.5, false)) still++
    }
    expect(seen.size).toBeGreaterThan(2)
    expect(still).toBeGreaterThan(150)
  })
})
