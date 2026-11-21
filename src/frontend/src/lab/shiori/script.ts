import type { BoardItem } from './board'
import { faceReducer, type FaceEvent, type FaceState, type Mood } from './faceMachine'
import type { Mode } from './modes'

/** A word of an answer, or a note it cites: [[Folder/name]] in the text. */
export type Token =
  | { type: 'word'; text: string; glued: boolean }
  | { type: 'cite'; path: string; glued: boolean }

/** Splits an answer into the pieces that stream in one by one; a citation is one piece. */
export function tokenize(text: string): Token[] {
  const out: Token[] = []
  const re = /\[\[([^\]]+)\]\]|[^\s[]+|\[/g
  let m: RegExpExecArray | null
  while ((m = re.exec(text)) !== null) {
    const glued = m.index > 0 && !/\s/.test(text[m.index - 1])
    if (m[1] !== undefined) out.push({ type: 'cite', path: m[1], glued })
    else out.push({ type: 'word', text: m[0], glued })
  }
  return out
}

export function citationsOf(text: string): string[] {
  return tokenize(text).flatMap((t) => (t.type === 'cite' ? [t.path] : []))
}

export type Step =
  | { kind: 'user'; text: string }
  | {
      kind: 'shiori'
      text: string
      mood: Mood
      /** Notes Shiori opens while thinking, shown before the answer. */
      reads?: string[]
      board?: Omit<BoardItem, 'id'>[]
      /** A button inside the answer offering a switch; Shiori never switches by itself. */
      offer?: Mode
      /** The answer carries a plan to approve. */
      approve?: boolean
    }
  | { kind: 'mode'; mode: Mode }

export interface Pace {
  charMs: number
  sendMs: number
  thinkMs: number
  readMs: number
  wordMs: number
  boardGapMs: number
  moodMs: number
  modeMs: number
  gapMs: number
}

export const PACE: Pace = {
  charMs: 28,
  sendMs: 350,
  thinkMs: 900,
  readMs: 450,
  wordMs: 85,
  boardGapMs: 650,
  moodMs: 1600,
  modeMs: 1200,
  gapMs: 700,
}

export type TimedStep =
  | { index: number; kind: 'user'; step: Extract<Step, { kind: 'user' }>; start: number; sentAt: number; end: number }
  | {
      index: number
      kind: 'shiori'
      step: Extract<Step, { kind: 'shiori' }>
      tokens: Token[]
      start: number
      speakStart: number
      speakEnd: number
      boardAt: number[]
      end: number
    }
  | { index: number; kind: 'mode'; step: Extract<Step, { kind: 'mode' }>; start: number; end: number }

export interface Timeline {
  steps: TimedStep[]
  events: { t: number; event: FaceEvent }[]
  duration: number
  startMode: Mode
  pace: Pace
}

export function buildTimeline(steps: Step[], startMode: Mode = 'brainstorm', pace: Pace = PACE): Timeline {
  const timed: TimedStep[] = []
  const events: { t: number; event: FaceEvent }[] = []
  let t = 600
  steps.forEach((step, index) => {
    if (step.kind === 'user') {
      const sentAt = t + step.text.length * pace.charMs + pace.sendMs
      timed.push({ index, kind: 'user', step, start: t, sentAt, end: sentAt })
      events.push({ t, event: { type: 'hear' } }, { t: sentAt, event: { type: 'send' } })
      t = sentAt
    } else if (step.kind === 'shiori') {
      const tokens = tokenize(step.text)
      const speakStart = t + pace.thinkMs + (step.reads?.length ?? 0) * pace.readMs
      const speakEnd = speakStart + tokens.length * pace.wordMs
      const boardAt = (step.board ?? []).map((_, k) => speakEnd + (k + 1) * pace.boardGapMs)
      const moodEnd = speakEnd + pace.moodMs
      const end = Math.max(moodEnd, boardAt.at(-1) ?? 0)
      timed.push({ index, kind: 'shiori', step, tokens, start: t, speakStart, speakEnd, boardAt, end })
      events.push(
        { t: speakStart, event: { type: 'speak' } },
        { t: speakEnd, event: { type: 'finish', mood: step.mood } },
        { t: moodEnd, event: { type: 'settle' } },
      )
      t = end + pace.gapMs
    } else {
      timed.push({ index, kind: 'mode', step, start: t, end: t + pace.modeMs })
      t += pace.modeMs
    }
  })
  return { steps: timed, events, duration: t, startMode, pace }
}

export type Entry =
  | { kind: 'user'; id: string; at: number; text: string }
  | {
      kind: 'shiori'
      id: string
      at: number
      tokens: Token[]
      shown: number
      thinking: boolean
      streaming: boolean
      reads: string[]
      offer?: Mode
      approve?: boolean
    }
  | { kind: 'mode'; id: string; at: number; mode: Mode; by: 'script' | 'you' }

export interface ManualSwitch {
  t: number
  mode: Mode
}

export interface Snapshot {
  entries: Entry[]
  composer: string
  mode: Mode
  board: BoardItem[]
  face: FaceState
  links: string[]
  done: boolean
}

/** Everything the screen shows at time t: the page is a pure function of the clock. */
export function snapshot(tl: Timeline, t: number, manual: ManualSwitch[] = []): Snapshot {
  const entries: Entry[] = []
  const board: BoardItem[] = []
  const links: string[] = []
  const switches: ManualSwitch[] = [...manual]
  let composer = ''

  for (const s of tl.steps) {
    if (t < s.start) break
    if (s.kind === 'user') {
      if (t >= s.sentAt) entries.push({ kind: 'user', id: `u${s.index}`, at: s.sentAt, text: s.step.text })
      else composer = s.step.text.slice(0, Math.floor((t - s.start) / tl.pace.charMs))
    } else if (s.kind === 'shiori') {
      const shown = t < s.speakStart ? 0 : Math.min(s.tokens.length, Math.floor((t - s.speakStart) / tl.pace.wordMs) + 1)
      entries.push({
        kind: 'shiori',
        id: `s${s.index}`,
        at: s.start,
        tokens: s.tokens,
        shown,
        thinking: t < s.speakStart,
        streaming: t >= s.speakStart && t < s.speakEnd,
        reads: s.step.reads ?? [],
        offer: s.step.offer,
        approve: s.step.approve,
      })
      for (const tok of s.tokens.slice(0, shown)) if (tok.type === 'cite') links.push(tok.path)
      s.boardAt.forEach((at, k) => {
        if (t >= at) board.push({ id: `b${s.index}-${k}`, ...s.step.board![k] })
      })
    } else {
      switches.push({ t: s.start, mode: s.step.mode })
      entries.push({ kind: 'mode', id: `m${s.index}`, at: s.start, mode: s.step.mode, by: 'script' })
    }
  }

  manual.forEach((m, k) => {
    if (m.t <= t) entries.push({ kind: 'mode', id: `x${k}`, at: m.t, mode: m.mode, by: 'you' })
  })
  entries.sort((a, b) => a.at - b.at)

  let mode = tl.startMode
  for (const sw of switches.filter((x) => x.t <= t).sort((a, b) => a.t - b.t)) mode = sw.mode

  const face = tl.events.filter((e) => e.t <= t).reduce((st, e) => faceReducer(st, e.event), 'idle' as FaceState)

  return { entries, composer, mode, board, face, links: [...new Set(links)], done: t >= tl.duration }
}

/** A moment of a step, to jump the clock there: when it starts, halfway through, or when it ends. */
export function timeOf(tl: Timeline, index: number, at: 'start' | 'mid' | 'end' = 'start'): number {
  const s = tl.steps.find((x) => x.index === index)
  if (!s) return tl.duration
  if (at === 'start') return s.start
  if (at === 'end') return s.end
  if (s.kind === 'shiori') return (s.speakStart + s.speakEnd) / 2
  if (s.kind === 'user') return (s.start + s.sentAt) / 2
  return (s.start + s.end) / 2
}

/** Made-up session: two desks merged at once and broke main. Every note named here is invented. */
export const SESSION: Step[] = [
  {
    kind: 'user',
    text: 'Two desks finished at the same time yesterday and the second merge broke main. How do we stop that?',
  },
  {
    kind: 'shiori',
    reads: ['Decisions/2026-11-20-merge-order', 'Sessions/2026-11-19-release-day'],
    text:
      'We already agreed that merges land one at a time, oldest first [[Decisions/2026-11-20-merge-order]]. ' +
      'Yesterday the second desk had rebased before the first merge landed, so its tests ran against an old main ' +
      '[[Sessions/2026-11-19-release-day]]. A few ways out: a merge queue per office, tests that run again after ' +
      'each merge, or the reviewer choosing the order.',
    mood: 'pleased',
    board: [
      { kind: 'idea', text: 'A merge queue per office' },
      { kind: 'idea', text: 'Run the tests again on the new main before landing' },
      { kind: 'doubt', text: 'Should the reviewer choose the order?' },
    ],
  },
  { kind: 'user', text: 'I like the queue. But what if a merge waits in line for hours?' },
  {
    kind: 'shiori',
    reads: ['Rooms/Backend/briefing'],
    text:
      'It should not wait long: the backend tests take about four minutes [[Rooms/Backend/briefing]]. ' +
      'I am not sure about long chains, though. Five desks in a row would be twenty minutes, and nobody ' +
      'has measured the frontend yet. Ready to decide? I can propose a rule.',
    mood: 'unsure',
    offer: 'decide',
    board: [{ kind: 'doubt', text: 'Long chains: five desks in a row is about 20 minutes' }],
  },
  { kind: 'mode', mode: 'decide' },
  { kind: 'user', text: 'Yes, propose it.' },
  {
    kind: 'shiori',
    text:
      'Proposed rule: merges into main go through one queue per office, oldest first, and each one runs the ' +
      'tests again on the new main before it lands. If the queue is longer than three, the person is told. ' +
      'Review it in thirty days [[Decisions/2026-11-20-merge-order]].',
    mood: 'pleased',
    board: [{ kind: 'decision', text: 'One merge queue per office, oldest first; tests run again on the new main' }],
  },
  { kind: 'mode', mode: 'organise' },
  { kind: 'user', text: 'Good. Turn it into work.' },
  {
    kind: 'shiori',
    reads: ['Rooms/Frontend/briefing'],
    text:
      'Here is the plan, waiting for your approval: the queue in the supervisor, a test run after each merge, ' +
      'and a badge on the desks waiting in line [[Rooms/Frontend/briefing]].',
    mood: 'pleased',
    approve: true,
    board: [
      { kind: 'task', text: 'Merge queue in the supervisor', office: 'Backend' },
      { kind: 'task', text: 'Run the tests again after each merge', office: 'Backend' },
      { kind: 'task', text: 'Badge on desks waiting in line', office: 'Frontend' },
    ],
  },
]

export const SESSION_TITLE = 'Merge queue per office'
export const SESSION_DATE = '2026-11-21'
