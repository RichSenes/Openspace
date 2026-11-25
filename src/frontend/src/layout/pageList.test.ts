import { describe, expect, it } from 'vitest'
import { GROUPS, NAV, PAGES, pageFromHash } from './pageList'

describe('pageFromHash', () => {
  it('reads a page from the address', () => {
    expect(pageFromHash('#board')).toBe('board')
    expect(pageFromHash('#/brain')).toBe('brain')
    expect(pageFromHash('#Settings')).toBe('settings')
    expect(pageFromHash('#lab-edit')).toBe('lab-edit')
  })

  it('opens on the world when the address names no page', () => {
    expect(pageFromHash('')).toBeNull()
    expect(pageFromHash('#')).toBeNull()
    expect(pageFromHash('#nowhere')).toBeNull()
  })
})

describe('the bar', () => {
  it('lists every page once, each with an icon and a hint', () => {
    const ids = PAGES.map((p) => p.id)
    expect(new Set(ids).size).toBe(ids.length)
    expect(ids).toHaveLength(14)
    for (const p of PAGES) {
      expect(p.icon).toBeTruthy()
      expect(p.hint.length).toBeGreaterThan(0)
    }
  })

  it('keeps Shiori and her brain as buttons of their own, the rest in menus', () => {
    expect(NAV.flatMap((n) => ('page' in n ? [n.page.id] : []))).toEqual(['shiori', 'brain'])
    expect(NAV.flatMap((n) => ('group' in n ? [n.group.label] : []))).toEqual(['Work', 'Offices', 'World', 'System'])
  })

  it('has a place for editing the lab and choosing its world', () => {
    const world = GROUPS.find((g) => g.label === 'World')
    expect(world?.pages.map((p) => p.id)).toEqual(['lab-edit', 'worlds'])
  })
})
