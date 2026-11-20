import { describe, expect, it } from 'vitest'
import { kindRows, obsidianLink } from './brain'

describe('the brain summary', () => {
  it('lists the known kinds in order, then the others', () => {
    expect(kindRows({ note: 2, zine: 1, decision: 4, rule: 1 }).map((r) => r.label)).toEqual(['Rules', 'Decisions', 'Other notes', 'zine'])
  })

  it('opens the folder in Obsidian', () => {
    expect(obsidianLink('/home/you/brain notes')).toBe('obsidian://open?path=%2Fhome%2Fyou%2Fbrain%20notes')
  })
})
