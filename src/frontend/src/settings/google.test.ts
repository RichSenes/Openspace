import { describe, expect, it } from 'vitest'
import { allows } from './google'

describe('allows', () => {
  it('says in words what routines can use of an account', () => {
    expect(
      allows([
        'https://www.googleapis.com/auth/gmail.modify',
        'https://www.googleapis.com/auth/calendar.events',
        'https://www.googleapis.com/auth/tasks',
      ]),
    ).toBe('Routines can use its mail, calendar and tasks.')
    expect(allows(['https://www.googleapis.com/auth/gmail.modify'])).toBe('Routines can use its mail.')
    expect(allows([])).toMatch(/connect it again/)
  })
})
