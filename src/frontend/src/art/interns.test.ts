import { describe, expect, it } from 'vitest'
import { internItems, internLook } from './roomScene'
import { INTERN, PUFF } from './interns'
import { HAIR_COLORS, SKIN_TONES } from './palette'
import { problemsOf } from './sprite'
import { STATION } from './station'

const agent = { skin: 'fair', hair: 'brown', hairStyle: 'short', cloth: 'green' } as const
const intern = { id: 'toolu_1', what: 'Find the login form', since: '2015-06-24T09:00:00Z' }

describe('interns', () => {
  it('are well drawn, and so is their puff', () => {
    expect(problemsOf(INTERN)).toEqual([])
    expect(problemsOf(PUFF)).toEqual([])
  })

  it('wear their agent colours with a skin and hair of their own', () => {
    const look = internLook(agent, 'toolu_1')
    expect(look.cloth).toBe('green')
    expect(Object.keys(SKIN_TONES)).toContain(look.skin)
    expect(Object.keys(HAIR_COLORS)).toContain(look.hair)
    expect(internLook(agent, 'toolu_1')).toEqual(look)
  })

  it('resolve every colour, whoever they are', () => {
    for (const id of ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h']) {
      const [item] = internItems(agent, [{ intern: { ...intern, id }, spot: 0, visible: true }], 0, 0, 0)
      expect(item.pixels.length).toBeGreaterThan(100)
      expect(item.pixels.every((p) => /^#[0-9a-f]{6}$/.test(p.color))).toBe(true)
    }
  })

  it('stand beside the desk, in front of it, right then left, and fit between two desks', () => {
    const items = internItems(agent, [{ intern, spot: 0, visible: true }, { intern: { ...intern, id: 'b' }, spot: 1, visible: true }], 0, 0, 0)
    const [right, left] = items
    expect(right.x).toBeGreaterThan(STATION.width / 2)
    expect(left.x).toBeLessThan(0)
    for (const item of items) expect(item.y + item.height).toBeGreaterThan(STATION.height)
    // Desks stand 48 pixels apart: interns of two neighbours never overlap.
    expect(right.x + INTERN.width).toBeLessThanOrEqual(48 + left.x)
  })

  it('arrive in a puff drawn over them, with sparks that glow', () => {
    const items = internItems(agent, [{ intern, spot: 0, visible: false, puff: 1 }], 0, 0, 0)
    expect(items).toHaveLength(1)
    expect(items[0].height).toBe(PUFF.height)
    expect(items[0].pixels.some((p) => p.glow)).toBe(true)
  })
})
