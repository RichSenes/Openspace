import { describe, expect, it } from 'vitest'
import type { RoleView } from '../api/types'
import { EMPTY_ROLE, formFrom, permissionSummary, skillsSummary, toggleSkill } from './roleForm'

const auditor: RoleView = {
  id: 'r1',
  name: 'Auditor',
  description: 'Reviews changes',
  instructions: 'Never change code.',
  model: null,
  edit: 'Ask',
  bash: 'Deny',
  web: 'Allow',
  helpers: 'Deny',
  skills: null,
}

describe('permissionSummary', () => {
  it('names only the limits', () => {
    expect(permissionSummary(auditor)).toBe('Change files: asks you · Run commands: not allowed')
  })

  it('says when an agent works freely', () => {
    const free = { ...EMPTY_ROLE, bash: 'Allow' as const }
    expect(permissionSummary(free)).toBe('Works freely')
    expect(permissionSummary({ ...free, helpers: 'Allow' })).toBe('Works freely · Can call in interns')
    expect(permissionSummary({ ...EMPTY_ROLE, bash: 'Deny', helpers: 'Allow' })).toBe('Run commands: not allowed · Can call in interns')
  })
})

describe('EMPTY_ROLE', () => {
  it('starts a new role asking before commands', () => {
    expect(permissionSummary(EMPTY_ROLE)).toBe('Run commands: asks you')
  })
})

describe('formFrom', () => {
  it('copies a role without its id, or starts empty', () => {
    expect(formFrom(auditor)).not.toHaveProperty('id')
    expect(formFrom(auditor).instructions).toBe('Never change code.')
    expect(formFrom(undefined)).toEqual(EMPTY_ROLE)
  })
})

describe('skills', () => {
  it('says what a role gives its agents', () => {
    expect(skillsSummary(null)).toBe('Every installed skill')
    expect(skillsSummary([])).toBe('No skills')
    expect(skillsSummary(['security-audit'])).toBe('Skill: security-audit')
    expect(skillsSummary(['security-audit', 'tdd'])).toBe('2 skills')
  })

  it('adds and removes one skill, sorted', () => {
    expect(toggleSkill(['tdd'], 'security-audit')).toEqual(['security-audit', 'tdd'])
    expect(toggleSkill(['security-audit', 'tdd'], 'tdd')).toEqual(['security-audit'])
  })
})
