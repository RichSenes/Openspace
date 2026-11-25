import { describe, expect, it } from 'vitest'
import { planLab, reachable } from './furnish'
import type { OfficeSpec } from './layout'
import { aloneAct, choosePastime, comfortsFor, COMFORTS, pastimeWeights } from './leisure'

const LAB: OfficeSpec[] = [
  { name: 'Alpha', accent: '#5b9bff', agents: 8, flavor: 'code' },
  { name: 'Beta', accent: '#f2a948', agents: 2, flavor: 'creative' },
  { name: 'Gamma', accent: '#3fd29a', agents: 4, flavor: 'research' },
  { name: 'Delta', accent: '#ff7aa8', agents: 1, flavor: 'data' },
  { name: 'Epsilon', accent: '#c48bff', agents: 3, flavor: 'code' },
  { name: 'Zeta', accent: '#e0e060', agents: 2, flavor: 'creative' },
]

describe('comfort corners', () => {
  it('gives each office two different kinds, the same every time', () => {
    for (const o of LAB) {
      const kinds = comfortsFor(o.name, o.flavor)
      expect(kinds).toHaveLength(2)
      expect(kinds[0]).not.toBe(kinds[1])
      expect(kinds.every((k) => COMFORTS.includes(k))).toBe(true)
      expect(comfortsFor(o.name, o.flavor)).toEqual(kinds)
    }
  })

  it('does not give every office the same corner', () => {
    const sets = new Set(LAB.map((o) => comfortsFor(o.name, o.flavor).join()))
    expect(sets.size).toBeGreaterThan(2)
  })

  it('puts a corner in every office, its places reachable, desks still reachable', () => {
    const { plan, furnished } = planLab(LAB)
    const shiori = furnished.spots.find((s) => s.use === 'shiori')!
    const reach = reachable(plan, furnished.blocked, [Math.floor(shiori.a / 8), Math.floor(shiori.b / 8)])
    for (const room of plan.spaces.filter((s) => s.kind === 'office')) {
      const nooks = furnished.spots.filter((s) => s.use === 'nook' && s.room === room.id)
      expect(nooks.length, room.name).toBeGreaterThan(0)
      expect(nooks.every((s) => s.does)).toBe(true)
      const seats = furnished.spots.filter((s) => s.use === 'seat' && s.room === room.id)
      expect(seats.length, room.name).toBeGreaterThanOrEqual(room.agents)
      for (const s of [...nooks, ...seats]) expect(reach.has(Math.floor(s.b / 8) * plan.w + Math.floor(s.a / 8)), room.name).toBe(true)
    }
  })
})

describe('free time', () => {
  it('weighs every pastime, summing to one', () => {
    const w = pastimeWeights([])
    expect(Object.values(w).reduce((s, x) => s + x, 0)).toBeCloseTo(1, 6)
    expect(Object.values(w).every((x) => x > 0)).toBe(true)
  })

  it('leans by character: the chatty visit, the shy keep to themselves, the curious look round', () => {
    const base = pastimeWeights([])
    expect(pastimeWeights(['Chatty']).visit).toBeGreaterThan(base.visit)
    expect(pastimeWeights(['Shy']).visit).toBeLessThan(base.visit)
    expect(pastimeWeights(['Shy']).alone).toBeGreaterThan(base.alone)
    expect(pastimeWeights(['Curious']).tour).toBeGreaterThan(base.tour)
  })

  it('spreads free time beyond the break room', () => {
    const counts = new Map<string, number>()
    for (let i = 0; i < 1000; i++) {
      const p = choosePastime([], i / 1000)
      counts.set(p, (counts.get(p) ?? 0) + 1)
    }
    expect(counts.get('games')!).toBeLessThan(400)
    for (const p of ['tour', 'visit', 'nook', 'alone']) expect(counts.get(p) ?? 0, p).toBeGreaterThan(80)
  })

  it('does something alone: the phone, a book, a coffee or a stretch', () => {
    const poses = new Set([0, 0.4, 0.7, 0.9].map((x) => aloneAct(x).pose))
    expect(poses).toEqual(new Set(['phone', 'book', 'coffee', 'stretch']))
  })
})
