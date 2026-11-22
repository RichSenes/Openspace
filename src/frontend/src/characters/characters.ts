/** The character monitor's data and the pure rules that turn it into words and shapes. */

export type Trait = 'Calm' | 'Chatty' | 'Meticulous' | 'Bold' | 'Shy' | 'Sunny' | 'DryHumoured' | 'Curious'
export type Shift = 'Always' | 'Day' | 'Night'
export type TrustLevel = 'Alone' | 'Tells' | 'AsksFirst'
export type TrustedAction = 'SafeRequests' | 'Journal' | 'Proposals'
export type TieKind = 'Reviewed' | 'SameFiles' | 'FixedFailure' | 'MergeConflict'

export interface BirthdayView {
  month: number
  day: number
  /** The person chose it; otherwise it comes from the desk's id. */
  chosen: boolean
}

export interface MoodPoint {
  at: string
  value: number
  change: number
  reason: string
}

export interface MoodLine {
  baseline: number
  value: number
  history: MoodPoint[]
}

export interface SeniorityView {
  roomId: string
  roomName: string
  done: number
  merged: number
  expert: boolean
}

export interface CharacterView {
  deskId: string
  name: string
  roomId: string
  roomName: string
  traits: Trait[]
  birthday: BirthdayView
  shift: Shift
  trust: Partial<Record<TrustedAction, TrustLevel>>
  seniority: SeniorityView[]
  mood: MoodLine
}

export interface TieEvent {
  at: string
  kind: TieKind
  change: number
  text: string
}

export interface RelationshipView {
  firstId: string
  firstName: string
  secondId: string
  secondName: string
  affinity: number
  history: TieEvent[]
}

export interface RoomExpertView {
  roomId: string
  roomName: string
  deskId: string
  deskName: string
  done: number
}

export interface CharacterBoardView {
  officeId: string
  officeName: string
  characters: CharacterView[]
  experts: RoomExpertView[]
  relationships: RelationshipView[]
}

export const TRAIT_LABELS: Record<Trait, string> = {
  Calm: 'Calm',
  Chatty: 'Chatty',
  Meticulous: 'Meticulous',
  Bold: 'Bold',
  Shy: 'Shy',
  Sunny: 'Sunny',
  DryHumoured: 'Dry-humoured',
  Curious: 'Curious',
}

export const SHIFTS: { id: Shift; label: string }[] = [
  { id: 'Always', label: 'Around the clock' },
  { id: 'Day', label: 'Day (08–20)' },
  { id: 'Night', label: 'Night (20–08)' },
]

export const TRUST_LEVELS: { id: TrustLevel; label: string }[] = [
  { id: 'Alone', label: 'Does it alone' },
  { id: 'Tells', label: 'Does it and tells' },
  { id: 'AsksFirst', label: 'Asks first' },
]

export const TRUSTED_ACTIONS: { id: TrustedAction; label: string; hint: string }[] = [
  { id: 'SafeRequests', label: 'Safe requests', hint: 'Edits and harmless commands inside its own copy. Risky ones always ask you.' },
  { id: 'Journal', label: 'Journal lines', hint: 'Lines in its own journal in the brain. Asks first: each line waits for you under Needs you.' },
  { id: 'Proposals', label: 'Proposals', hint: 'Decisions and notes it proposes to the brain. Asks first: Shiori leaves them to you.' },
]

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']

/** "24 December": a birthday never has a year. */
export function birthdayLabel(b: Pick<BirthdayView, 'month' | 'day'>): string {
  return `${b.day} ${MONTHS[b.month - 1] ?? '?'}`
}

/** Days in a month of a leap year, so that 29 February can be chosen. */
export function daysIn(month: number): number {
  return new Date(2024, month, 0).getDate()
}

/** The level of an action, today's behaviour when it was never set. */
export function trustOf(c: Pick<CharacterView, 'trust'>, action: TrustedAction): TrustLevel {
  return c.trust[action] ?? 'Alone'
}

/** A word for a mood value, from 0 to 100. */
export function moodWord(value: number): string {
  if (value >= 75) return 'Cheerful'
  if (value >= 60) return 'Content'
  if (value >= 45) return 'So-so'
  if (value >= 30) return 'Low'
  return 'Down'
}

/** A word for an affinity, from -100 to 100. */
export function affinityWord(affinity: number): string {
  if (affinity >= 8) return 'Close'
  if (affinity >= 3) return 'Friendly'
  if (affinity > -3) return 'Acquainted'
  return 'Strained'
}

/** "+3", "-2", "0": a change with its sign. */
export function signed(n: number): string {
  return n > 0 ? `+${n}` : `${n}`
}

/**
 * The polyline of a mood's week, from its baseline seven days ago to now: x is time across
 * the box, y the value (100 at the top). A history with no change is a flat line at the value.
 */
export function moodPath(line: MoodLine, now: number, width: number, height: number, days = 7): string {
  const start = now - days * 86_400_000
  const x = (t: number) => Math.round(((t - start) / (now - start)) * width * 10) / 10
  const y = (v: number) => Math.round((1 - v / 100) * height * 10) / 10
  const points: [number, number][] = [[0, y(line.history.length ? line.baseline : line.value)]]
  let last = points[0][1]
  for (const p of line.history) {
    const at = Math.min(Math.max(new Date(p.at).getTime(), start), now)
    points.push([x(at), last], [x(at), y(p.value)])
    last = y(p.value)
  }
  points.push([width, last])
  return points.map(([px, py]) => `${px},${py}`).join(' ')
}

/** The relationships a desk is part of, with the other desk named. */
export function tiesOf(board: Pick<CharacterBoardView, 'relationships'>, deskId: string): { other: string; tie: RelationshipView }[] {
  return board.relationships
    .filter((r) => r.firstId === deskId || r.secondId === deskId)
    .map((r) => ({ other: r.firstId === deskId ? r.secondName : r.firstName, tie: r }))
}

export type AgendaLineKind = 'Task' | 'Routine' | 'Review' | 'Event' | 'Due'

export interface AgendaLine {
  /** "2026-11-22", on the office's clock. */
  date: string
  /** "20:00:00"; null for what has a day but no hour. */
  time: string | null
  kind: AgendaLineKind
  title: string
  detail: string
}

export interface AgendaView {
  officeId: string
  from: string
  until: string
  lines: AgendaLine[]
  /** The Google account whose calendar was read; null when none is connected with it. */
  calendar: string | null
  calendarProblem: string | null
}

export const AGENDA_KIND_LABELS: Record<AgendaLineKind, string> = {
  Task: 'Work',
  Routine: 'Routine',
  Review: 'Review',
  Event: 'Event',
  Due: 'To do',
}

/** A "2026-11-22" day as a local date: `new Date("2026-11-22")` would be midnight in London. */
export function localDay(date: string): Date {
  const [y, m, d] = date.split('-').map(Number)
  return new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1)
}

/** "20:00" from "20:00:00"; empty for a line with no hour. */
export function agendaTime(time: string | null): string {
  return time ? time.slice(0, 5) : ''
}

/** The agenda's lines grouped by day, in order; days with nothing are left out. */
export function agendaDays(view: Pick<AgendaView, 'lines'>): { date: string; lines: AgendaLine[] }[] {
  const days: { date: string; lines: AgendaLine[] }[] = []
  for (const line of view.lines) {
    const last = days.at(-1)
    if (last?.date === line.date) last.lines.push(line)
    else days.push({ date: line.date, lines: [line] })
  }
  return days
}
