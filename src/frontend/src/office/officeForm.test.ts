import { describe, expect, it } from 'vitest'
import type { OfficeView } from '../api/types'
import { changedParts, formOf, move, pathsOf, reserveChoices } from './officeForm'

const office = {
  id: 'o1',
  name: 'Shop',
  repositoryPath: '/repos/shop',
  theme: 'cozy',
  rooms: [],
  localOnly: false,
  privatePaths: ['/home/you/photos'],
  reserveModels: ['claude-code/sonnet', 'nvidia/qwen3-coder'],
} as OfficeView

describe('changedParts', () => {
  it('finds nothing to save in the office as it is', () => {
    expect(changedParts(office, formOf(office))).toEqual([])
    expect(changedParts(office, { ...formOf(office), name: ' Shop ', privatePaths: '\n/home/you/photos\n\n' })).toEqual([])
  })

  it('saves the repository first, then the rest that changed', () => {
    const form = { ...formOf(office), name: 'Shop website', repositoryPath: '/repos/web', localOnly: true, reserveModels: ['nvidia/qwen3-coder'] }
    expect(changedParts(office, form)).toEqual(['repository', 'name', 'localOnly', 'reserveModels'])
  })

  it('counts a new order of the reserves as a change', () => {
    expect(changedParts(office, { ...formOf(office), reserveModels: [...office.reserveModels!].reverse() })).toEqual(['reserveModels'])
  })
})

describe('the office form helpers', () => {
  it('reads one private folder per line', () => {
    expect(pathsOf(' /a \n\n/b')).toEqual(['/a', '/b'])
  })

  it('moves a reserve up or down, never past the ends', () => {
    expect(move(['a', 'b', 'c'], 2, -1)).toEqual(['a', 'c', 'b'])
    expect(move(['a', 'b'], 0, -1)).toEqual(['a', 'b'])
  })

  it('offers Claude Code models as claude-code/<model> before OpenCode ones', () => {
    const choices = reserveChoices([{ id: 'nvidia/qwen3-coder', name: 'Qwen3 Coder' }], [{ id: 'sonnet', name: 'Sonnet' }])
    expect(choices.map((c) => c.id)).toEqual(['claude-code/sonnet', 'nvidia/qwen3-coder'])
    expect(choices[0].name).toBe('Claude Code · Sonnet')
  })
})
