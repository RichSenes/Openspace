/**
 * Every date and time the interface shows, in one format: the words are English like the
 * rest of the interface, the date and the clock follow the browser's language (so 24 h or
 * AM/PM is the person's own choice). `locale` is only there for the tests.
 */

type When = string | Date

const toDate = (when: When) => (typeof when === 'string' ? new Date(when) : when)

const dayNumber = (d: Date) => Math.round(new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime() / 86_400_000)

/** How many calendar days from now: 0 today, -1 yesterday, 1 tomorrow. */
const daysFrom = (d: Date, now: Date) => dayNumber(d) - dayNumber(now)

/** "Fri 30 Oct", with the year only when it is not this one. */
function shortDate(d: Date, now: Date, locale?: string): string {
  return d.toLocaleDateString(locale, { weekday: 'short', day: 'numeric', month: 'short', year: d.getFullYear() === now.getFullYear() ? undefined : 'numeric' })
}

/** The clock time, always the same width: "09:05", or "09:05 AM" where the browser counts that way. */
export function formatTime(when: When, options: { seconds?: boolean } = {}, locale?: string): string {
  return toDate(when).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', second: options.seconds ? '2-digit' : undefined })
}

/** A day as a heading: Today, Yesterday, Tomorrow, else "Fri 30 Oct". */
export function formatDay(when: When, now: Date = new Date(), locale?: string): string {
  const d = toDate(when)
  const days = daysFrom(d, now)
  if (days === 0) return 'Today'
  if (days === -1) return 'Yesterday'
  if (days === 1) return 'Tomorrow'
  return shortDate(d, now, locale)
}

/** A moment as people say it: "today at 15:30", "yesterday at 23:05", "Fri 30 Oct at 06:00". */
export function formatMoment(when: When, now: Date = new Date(), locale?: string): string {
  const d = toDate(when)
  const days = daysFrom(d, now)
  const time = formatTime(d, {}, locale)
  if (days === 0) return `today at ${time}`
  if (days === -1) return `yesterday at ${time}`
  if (days === 1) return `tomorrow at ${time}`
  return `${shortDate(d, now, locale)} at ${time}`
}
