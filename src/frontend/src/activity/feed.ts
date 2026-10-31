import type { FeedLine } from '../api/types'
import { KIND_LABEL } from '../board/auditLabels'

/** The time of a line, always the same width: the day goes in the heading above it. */
export const feedTime = (at: string) => new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })

/** The heading of a day: Today, Yesterday, else the date. */
export function feedDay(at: string, now: Date): string {
  const when = new Date(at)
  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  if (when.toDateString() === now.toDateString()) return 'Today'
  if (when.toDateString() === yesterday.toDateString()) return 'Yesterday'
  return when.toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short' })
}

/** Lines grouped by day, in the order they come (newest first). */
export function byDay(lines: readonly FeedLine[], now: Date): { day: string; lines: FeedLine[] }[] {
  const days: { day: string; lines: FeedLine[] }[] = []
  for (const line of lines) {
    const day = feedDay(line.at, now)
    if (days.at(-1)?.day === day) days.at(-1)!.lines.push(line)
    else days.push({ day, lines: [line] })
  }
  return days
}

/** Who did it and where: the desk and its room, plus the office when the feed follows them all. */
export function feedPlace(line: FeedLine, withOffice: boolean): string {
  const place = [line.deskName, line.roomName].filter(Boolean).join(' · ') || 'No desk'
  return withOffice ? `${line.officeName} · ${place}` : place
}

/** The kind of a line, in words; a failed command or change says so in words too, not only in red. */
export function feedKind(line: FeedLine): string {
  return line.succeeded === false ? `${KIND_LABEL[line.kind]} failed` : KIND_LABEL[line.kind]
}

/** What happened, with the start of a note's text. */
export const feedText = (line: FeedLine) => (line.excerpt ? `${line.summary}: ${line.excerpt}` : line.summary)

/** How many more lines "Show older" asks for, up to what the server gives at once. */
export const FEED_PAGE = 50
export const FEED_MAX = 200

export const moreLines = (limit: number) => Math.min(FEED_MAX, limit + FEED_PAGE)
