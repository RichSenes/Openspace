import type { RoutineRunStatus, ScheduleView, Weekday } from '../api/types'

/** Monday first, as the person's week reads. */
export const WEEK: { day: Weekday; short: string }[] = [
  { day: 'Monday', short: 'Mon' },
  { day: 'Tuesday', short: 'Tue' },
  { day: 'Wednesday', short: 'Wed' },
  { day: 'Thursday', short: 'Thu' },
  { day: 'Friday', short: 'Fri' },
  { day: 'Saturday', short: 'Sat' },
  { day: 'Sunday', short: 'Sun' },
]

const WEEKDAYS: Weekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']

const plural = (day: Weekday) => `${day}s`

/** When it runs, in words: "Every day at 07:00", "Thursdays at 09:00", "Every 2 weeks on Monday at 08:30". */
export function describeSchedule(schedule: ScheduleView): string {
  const days = WEEK.map((w) => w.day).filter((d) => schedule.days.includes(d))
  const at = `at ${schedule.at}`
  if (days.length === 0 || days.length === 7) return `Every day ${at}`
  if (schedule.everyWeeks > 1) {
    return `Every ${schedule.everyWeeks} weeks on ${listOf(days)} ${at}`
  }

  if (days.length === 5 && WEEKDAYS.every((d) => days.includes(d))) return `Weekdays ${at}`
  return `${listOf(days.map(plural))} ${at}`
}

function listOf(items: string[]): string {
  return items.length <= 1 ? (items[0] ?? '') : `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`
}

/** Turns a day on or off, keeping the week in order. */
export function toggleDay(days: Weekday[], day: Weekday): Weekday[] {
  const next = days.includes(day) ? days.filter((d) => d !== day) : [...days, day]
  return WEEK.map((w) => w.day).filter((d) => next.includes(d))
}

/** Every few weeks needs days to count on; with none it falls back to every week. */
export function scheduleFrom(days: Weekday[], at: string, everyWeeks: number): ScheduleView {
  return { days, at, everyWeeks: days.length === 0 ? 1 : everyWeeks }
}

export const RUN_LABEL: Record<RoutineRunStatus, string> = { Running: 'Running now', Succeeded: 'Done', Failed: 'Failed' }

/** Whether the settings text is a JSON object, as routines take it; the problem in words otherwise. */
export function settingsProblem(text: string): string | undefined {
  if (!text.trim()) return undefined
  try {
    const value: unknown = JSON.parse(text)
    return value !== null && typeof value === 'object' && !Array.isArray(value) ? undefined : 'Settings must be a JSON object, like {"sources": []}.'
  } catch {
    return 'These settings are not valid JSON.'
  }
}
