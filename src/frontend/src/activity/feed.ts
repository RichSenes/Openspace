import type { FeedLine } from '../api/types'
import { KIND_LABEL } from '../board/auditLabels'

/** The time of a line: the hour today, the day and hour before today. */
export function feedTime(at: string, now: Date): string {
  const when = new Date(at)
  const time = when.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  return when.toDateString() === now.toDateString() ? time : `${when.toLocaleDateString([], { day: 'numeric', month: 'short' })} ${time}`
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
