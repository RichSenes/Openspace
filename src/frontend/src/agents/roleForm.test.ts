import { describe, expect, it } from 'vitest'
import type { RoleView } from '../api/types'
import { EMPTY_ROLE, formFrom, permissionSummary } from './roleForm'

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
}

describe('permissionSummary', () => {
  it('names only the limits', () => {
    expect(permissionSummary(auditor)).toBe('Change files: asks you · Run commands: not allowed')
  })

  it('says when an agent works freely', () => {
    expect(permissionSummary(EMPTY_ROLE)).toBe('Works freely')
    expect(permissionSummary({ ...EMPTY_ROLE, helpers: 'Allow' })).toBe('Works freely · Can call in interns')
    expect(permissionSummary({ ...EMPTY_ROLE, bash: 'Deny', helpers: 'Allow' })).toBe('Run commands: not allowed · Can call in interns')
  })
})

describe('formFrom', () => {
  it('copies a role without its id, or starts empty', () => {
    expect(formFrom(auditor)).not.toHaveProperty('id')
    expect(formFrom(auditor).instructions).toBe('Never change code.')
    expect(formFrom(undefined)).toEqual(EMPTY_ROLE)
  })
})
