import { describe, expect, it } from 'vitest'
import { describeSchedule, scheduleFrom, settingsProblem, toggleDay } from './schedule'

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
