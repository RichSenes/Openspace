import { describe, expect, it } from 'vitest'
import { findPath, furnish, planLab } from './furnish'
import { makePlan, roomSize, wallBetween, type OfficeSpec } from './layout'
import { stormAt } from './sky'

const SETUPS: OfficeSpec[][] = [
  [
    { name: 'Karuta', accent: '#5b9bff', agents: 5, flavor: 'code' },
    { name: 'Openspace', accent: '#f2a948', agents: 3, flavor: 'code' },
    { name: 'Riku', accent: '#3fd29a', agents: 2, flavor: 'research' },
    { name: 'Atelier', accent: '#ff7aa8', agents: 3, flavor: 'creative' },
  ],
  [{ name: 'Solo', accent: '#5b9bff', agents: 1, flavor: 'data' }],
  [
    { name: 'Alpha', accent: '#5b9bff', agents: 8, flavor: 'code' },
    { name: 'Beta', accent: '#f2a948', agents: 2, flavor: 'creative' },
    { name: 'Gamma', accent: '#3fd29a', agents: 4, flavor: 'research' },
    { name: 'Delta', accent: '#ff7aa8', agents: 1, flavor: 'data' },
    { name: 'Epsilon', accent: '#c48bff', agents: 3, flavor: 'code' },
    { name: 'Zeta', accent: '#e0e060', agents: 2, flavor: 'creative' },
  ],
]

describe('the lab made from a setup', () => {
  it('gives bigger rooms to offices with more agents, and the same lab for the same setup', () => {
    const [a1, b1] = roomSize({ name: 'X', accent: '#000000', agents: 2, flavor: 'code' })
    const [a2, b2] = roomSize({ name: 'X', accent: '#000000', agents: 6, flavor: 'code' })
    expect(a2 * b2).toBeGreaterThan(a1 * b1)
    const one = makePlan(SETUPS[0])
    const two = makePlan(SETUPS[0])
    expect([...one.cells]).toEqual([...two.cells])
  })

  for (const setup of SETUPS) {
    it(`plans ${setup.length} offices without rooms overlapping, each with a door`, () => {
      const plan = makePlan(setup)
      const rooms = plan.spaces.filter((s) => s.kind === 'office' || s.kind === 'lounge')
      expect(rooms).toHaveLength(setup.length + 1)
      for (let i = 0; i < rooms.length; i++)
        for (let j = i + 1; j < rooms.length; j++) {
          const x = rooms[i]
          const y = rooms[j]
          const overlap = x.a0 < y.a1 && y.a0 < x.a1 && x.b0 < y.b1 && y.b0 < x.b1
          expect(overlap, `${x.name} and ${y.name}`).toBe(false)
        }
      for (const room of rooms) {
        let doors = 0
        for (let b = room.b0 - 1; b < room.b1; b++)
          for (let a = room.a0 - 1; a < room.a1; a++)
            for (const dir of ['a', 'b'] as const) {
              const inA = (x: number, y: number) => x >= room.a0 && x < room.a1 && y >= room.b0 && y < room.b1
              const crosses = inA(a, b) !== inA(dir === 'a' ? a + 1 : a, dir === 'b' ? b + 1 : b)
              if (crosses && !wallBetween(plan, a, b, dir)) doors++
            }
        expect(doors, room.name).toBeGreaterThan(0)
      }
    })

    it(`seats every agent of ${setup.length} offices, and everyone can walk from their seat to Shiori`, () => {
      const { plan, furnished: f } = planLab(setup)
      const seats = f.spots.filter((s) => s.use === 'seat')
      expect(seats.length).toBe(setup.reduce((n, o) => n + o.agents, 0))
      const shiori = f.spots.filter((s) => s.use === 'shiori')
      expect(shiori.length).toBeGreaterThan(4)
      const tile = (s: { a: number; b: number }): [number, number] => [Math.floor(s.a / 8), Math.floor(s.b / 8)]
      for (const seat of seats) expect(findPath(plan, f.blocked, tile(seat), tile(shiori[0])), plan.spaces[seat.room].name).not.toBeNull()
    })
  }

  it('walks along tiles without crossing walls', () => {
    const plan = makePlan(SETUPS[0])
    const f = furnish(plan)
    const seats = f.spots.filter((s) => s.use === 'seat')
    const path = findPath(plan, f.blocked, [Math.floor(seats[0].a / 8), Math.floor(seats[0].b / 8)], [Math.floor(seats.at(-1)!.a / 8), Math.floor(seats.at(-1)!.b / 8)])!
    for (let i = 1; i < path.length; i++) {
      const [a0, b0] = path[i - 1]
      const [a1, b1] = path[i]
      expect(Math.abs(a1 - a0) + Math.abs(b1 - b0)).toBe(1)
      const dir = a1 !== a0 ? 'a' : 'b'
      expect(wallBetween(plan, Math.min(a0, a1), Math.min(b0, b1), dir)).toBe(false)
    }
  })

  it('keeps storms rare: lightning and stuttering lamps only now and then', () => {
    let flashes = 0
    let down = 0
    for (let t = 0; t < 600; t += 0.05) {
      const s = stormAt(t, 'storm')
      if (s.flash > 0) flashes++
      if (s.lampsDown) down++
      expect(stormAt(t, 'clear')).toEqual({ flash: 0, lampsDown: false })
    }
    expect(flashes).toBeGreaterThan(0)
    expect(flashes / 12000).toBeLessThan(0.05)
    expect(down / 12000).toBeLessThan(0.03)
  })
})
