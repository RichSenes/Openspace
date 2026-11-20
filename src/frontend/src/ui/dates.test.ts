import { describe, expect, it } from 'vitest'
import { formatDay, formatMoment, formatTime } from './dates'

const now = new Date(2026, 9, 27, 10, 0)
const at = (day: number, hour: number, minute = 0, year = 2026) => new Date(year, 9, day, hour, minute)

describe('formatMoment', () => {
  it('says today, yesterday and tomorrow in words, with the time', () => {
    expect(formatMoment(at(27, 15, 30), now, 'en-GB')).toBe('today at 15:30')
    expect(formatMoment(at(26, 23, 5), now, 'en-GB')).toBe('yesterday at 23:05')
    expect(formatMoment(at(28, 6), now, 'en-GB')).toBe('tomorrow at 06:00')
  })

  it('names the day further away, and the year only when it is not this one', () => {
    expect(formatMoment(at(30, 6), now, 'en-GB')).toBe('Fri 30 Oct at 06:00')
    expect(formatMoment(at(30, 6, 0, 2025), now, 'en-GB')).toBe('Thu, 30 Oct 2025 at 06:00')
  })

  it('takes the text the API sends', () => {
    expect(formatMoment(at(27, 15, 30).toISOString(), now, 'en-GB')).toBe('today at 15:30')
  })

  it('follows the browser locale for the date and the clock', () => {
    expect(formatMoment(at(27, 15, 30), now, 'en-US')).toBe('today at 03:30 PM')
    expect(formatMoment(at(30, 6), now, 'it-IT')).toMatch(/^ven 30 ott at 06:00$/)
  })
})

describe('formatDay', () => {
  it('names the near days and dates the others', () => {
    expect(formatDay(at(27, 23, 59), now, 'en-GB')).toBe('Today')
    expect(formatDay(at(26, 0, 1), now, 'en-GB')).toBe('Yesterday')
    expect(formatDay(at(28, 9), now, 'en-GB')).toBe('Tomorrow')
    expect(formatDay(at(20, 9), now, 'en-GB')).toBe('Tue 20 Oct')
  })
})

describe('formatTime', () => {
  it('keeps the same width morning and evening', () => {
    expect(formatTime(at(27, 9, 5), {}, 'en-GB')).toBe('09:05')
    expect(formatTime(at(27, 21, 45), {}, 'en-GB')).toBe('21:45')
  })

  it('adds the seconds when asked', () => {
    expect(formatTime(new Date(2026, 9, 27, 9, 5, 7), { seconds: true }, 'en-GB')).toBe('09:05:07')
  })
})
