import { describe, expect, it } from 'vitest'
import { READY_ROLES, readyRolesLeft } from './readyRoles'

describe('READY_ROLES', () => {
  it('covers the usual work of a project, each role with its instructions', () => {
    expect(READY_ROLES.map((r) => r.name)).toEqual(
      expect.arrayContaining(['Backend developer', 'Frontend developer', 'Database specialist', 'Tester', 'Writer', 'Reviewer']),
    )
    for (const role of READY_ROLES) {
      expect(role.description.length).toBeGreaterThan(10)
      expect(role.instructions.split('\n').length).toBeGreaterThanOrEqual(4)
    }
  })

  it('asks before commands, and who only reads changes no file', () => {
    expect(READY_ROLES.every((r) => r.bash === 'Ask')).toBe(true)
    expect(READY_ROLES.find((r) => r.name === 'Reviewer')?.edit).toBe('Deny')
  })
})

describe('readyRolesLeft', () => {
  it('leaves out the roles already added, whatever the case of their name', () => {
    const left = readyRolesLeft([{ name: 'tester ' }, { name: 'My own role' }])
    expect(left.map((r) => r.name)).not.toContain('Tester')
    expect(left).toHaveLength(READY_ROLES.length - 1)
    expect(readyRolesLeft(undefined)).toHaveLength(READY_ROLES.length)
  })
})
