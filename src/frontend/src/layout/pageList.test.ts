import { describe, expect, it } from 'vitest'
import { GROUPS, PAGES, pageFromHash } from './pageList'

describe('pageFromHash', () => {
  it('reads a page from the address', () => {
    expect(pageFromHash('#board')).toBe('board')
    expect(pageFromHash('#/brain')).toBe('brain')
    expect(pageFromHash('#Settings')).toBe('settings')
  })

  it('opens on the world when the address names no page', () => {
    expect(pageFromHash('')).toBeNull()
    expect(pageFromHash('#')).toBeNull()
    expect(pageFromHash('#nowhere')).toBeNull()
  })
})

describe('the page menu', () => {
  it('lists every page once, each with an icon and a hint', () => {
    const ids = PAGES.map((p) => p.id)
    expect(new Set(ids).size).toBe(ids.length)
    expect(ids).toHaveLength(12)
    for (const p of PAGES) {
      expect(p.icon).toBeTruthy()
      expect(p.hint.length).toBeGreaterThan(0)
    }
  })

  it('keeps the groups few', () => {
    expect(GROUPS.length).toBeLessThanOrEqual(4)
  })
})
