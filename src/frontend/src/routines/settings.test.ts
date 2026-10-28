import { describe, expect, it } from 'vitest'
import { readAgenda, readText, usesAccount, withAgenda, withText } from './settings'

describe('routine text settings', () => {
  it('reads a text setting, empty when missing or not text', () => {
    expect(readText('{"account":"someone@example.com"}', 'account')).toBe('someone@example.com')
    expect(readText('{"account":3}', 'account')).toBe('')
    expect(readText('{"account":', 'account')).toBe('')
  })

  it('changes one setting and keeps the others, leaving broken JSON alone', () => {
    expect(JSON.parse(withText('{"labels":[1],"account":""}', 'account', 'someone@example.com'))).toEqual({ labels: [1], account: 'someone@example.com' })
    expect(withText('{"account":', 'account', 'x')).toBe('{"account":')
  })
})

describe('mail agenda settings', () => {
  it('puts dates in both places unless told otherwise', () => {
    expect(readAgenda('{}')).toEqual({ tasks: true, calendar: true })
    expect(readAgenda('{"agenda":{"calendar":false}}')).toEqual({ tasks: true, calendar: false })
    expect(readAgenda('{"agenda":false}')).toEqual({ tasks: false, calendar: false })
  })

  it('writes both choices and keeps the rest', () => {
    expect(JSON.parse(withAgenda('{"account":"a"}', { tasks: false, calendar: true }))).toEqual({ account: 'a', agenda: { tasks: false, calendar: true } })
  })
})

describe('routines on a Google account', () => {
  it('asks for the account in mail, newsletter, follow-up and weekly routines only', () => {
    expect(['mail', 'newsletters', 'followup', 'weekly'].every(usesAccount)).toBe(true)
    expect(usesAccount('morning-paper')).toBe(false)
  })
})
