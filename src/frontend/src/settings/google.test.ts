import { describe, expect, it } from 'vitest'
import { allows, clientFileProblem, googleStep } from './google'

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

describe('google setup', () => {
  it('goes from the client file to a first account', () => {
    expect(googleStep(undefined)).toBe('client')
    expect(googleStep({ clientReady: false, accounts: [] })).toBe('client')
    expect(googleStep({ clientReady: true, accounts: [] })).toBe('account')
    expect(googleStep({ clientReady: true, accounts: [{}] })).toBe('done')
  })

  it('tells an OAuth client file from other files before sending it', () => {
    expect(clientFileProblem('{"installed":{"client_id":"made-up.apps.example","client_secret":"made-up"}}')).toBeNull()
    expect(clientFileProblem('{"web":{"client_id":"made-up.apps.example","client_secret":"made-up"}}')).toBeNull()
    expect(clientFileProblem('not json')).toMatch(/not valid JSON/)
    expect(clientFileProblem('[1]')).toMatch(/not an OAuth client/)
    expect(clientFileProblem('{"type":"service_account","private_key":"x"}')).toMatch(/service account/)
    expect(clientFileProblem('{"installed":{"client_id":"made-up"}}')).toMatch(/no client_secret/)
    expect(clientFileProblem('{"installed":{}}')).toMatch(/no client_id/)
  })
})
