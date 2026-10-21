import { describe, expect, it } from 'vitest'
import { retryDelay } from './retry'

describe('retryDelay', () => {
  it('backs off quickly at first', () => {
    expect([0, 1, 2, 3].map(retryDelay)).toEqual([1000, 2000, 4000, 8000])
  })

  it('settles at ten seconds and keeps trying forever', () => {
    expect(retryDelay(4)).toBe(10_000)
    expect(retryDelay(1000)).toBe(10_000)
  })
})
