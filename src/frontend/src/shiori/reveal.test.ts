import { describe, expect, it } from 'vitest'
import { READING_CPS, revealed } from './reveal'

describe('showing her answer at the pace it is read', () => {
  it('moves at reading speed', () => {
    expect(revealed(0, 1000, 1000)).toBe(READING_CPS)
    expect(revealed(10, 1000, 500)).toBe(10 + READING_CPS / 2)
  })

  it('never shows more than has arrived', () => {
    expect(revealed(95, 100, 10_000)).toBe(100)
  })
})
