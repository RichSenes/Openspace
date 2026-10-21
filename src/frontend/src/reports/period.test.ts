import { describe, expect, it } from 'vitest'
import { dayLabel, dayRange, today } from './period'

describe('dayRange', () => {
  it('spans exactly one local day', () => {
    const { from, to } = dayRange('2015-06-24')
    expect(new Date(to).getTime() - new Date(from).getTime()).toBeGreaterThanOrEqual(23 * 3600_000)
    expect(new Date(from).getDate()).toBe(24)
    expect(new Date(from).getHours()).toBe(0)
  })
})

describe('dayLabel', () => {
  it('names the day in the chosen language', () => {
    expect(dayLabel('2015-06-24', 'Italian')).toBe('24 giugno 2015')
    expect(dayLabel('2015-06-24', 'English')).toBe('24 June 2015')
  })
})

describe('today', () => {
  it('is a date input value', () => {
    expect(today()).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })
})
