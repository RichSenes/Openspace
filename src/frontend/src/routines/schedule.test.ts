import { describe, expect, it } from 'vitest'
import { describeSchedule, scheduleFrom, settingsProblem, toggleDay, whenNear } from './schedule'

describe('describeSchedule', () => {
  it('says every day when no day or every day is chosen', () => {
    expect(describeSchedule({ days: [], at: '07:00', everyWeeks: 1 })).toBe('Every day at 07:00')
  })

  it('names the days in week order', () => {
    expect(describeSchedule({ days: ['Thursday'], at: '09:00', everyWeeks: 1 })).toBe('Thursdays at 09:00')
    expect(describeSchedule({ days: ['Friday', 'Monday'], at: '18:30', everyWeeks: 1 })).toBe('Mondays and Fridays at 18:30')
    expect(describeSchedule({ days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'], at: '06:00', everyWeeks: 1 })).toBe('Weekdays at 06:00')
  })

  it('counts weeks for schedules every few weeks', () => {
    expect(describeSchedule({ days: ['Monday'], at: '08:30', everyWeeks: 2 })).toBe('Every 2 weeks on Monday at 08:30')
  })
})

describe('schedule editing', () => {
  it('keeps the week in order when days are toggled', () => {
    expect(toggleDay(['Thursday'], 'Monday')).toEqual(['Monday', 'Thursday'])
    expect(toggleDay(['Monday', 'Thursday'], 'Monday')).toEqual(['Thursday'])
  })

  it('runs every week when no day is chosen', () => {
    expect(scheduleFrom([], '07:00', 2)).toEqual({ days: [], at: '07:00', everyWeeks: 1 })
  })
})

describe('settingsProblem', () => {
  it('takes a JSON object or nothing, and says what is wrong otherwise', () => {
    expect(settingsProblem('{"sources":[]}')).toBeUndefined()
    expect(settingsProblem('')).toBeUndefined()
    expect(settingsProblem('[1, 2]')).toMatch(/JSON object/)
    expect(settingsProblem('{nope')).toMatch(/not valid JSON/)
  })
})

describe('whenNear', () => {
  const now = new Date(2026, 9, 27, 10, 0)
  const at = (day: number, hour: number, minute = 0, year = 2026) => new Date(year, 9, day, hour, minute).toISOString()

  it('says today, tomorrow and yesterday with the time on the 24 hour clock', () => {
    expect(whenNear(at(27, 15, 30), now)).toBe('today at 15:30')
    expect(whenNear(at(28, 6), now)).toBe('tomorrow at 06:00')
    expect(whenNear(at(26, 23, 5), now)).toBe('yesterday at 23:05')
  })

  it('names the day further away, and the year only when it is not this one', () => {
    expect(whenNear(at(30, 6), now)).toBe('Fri 30 Oct at 06:00')
    expect(whenNear(at(30, 6, 0, 2025), now)).toBe('Thu 30 Oct 2025 at 06:00')
  })
})
