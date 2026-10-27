import { describe, expect, it } from 'vitest'
import { readText, withText } from './settings'

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
