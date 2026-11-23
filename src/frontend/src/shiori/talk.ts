import type { ShioriMode, ShioriSessionSummary, TranscriptEntry, WhiteboardItem } from '../api/types'
import type { BoardItem, BoardKind } from '../lab/shiori/board'
import type { FaceState } from '../lab/shiori/faceMachine'
import type { Mode } from '../lab/shiori/modes'
import { tokenize, type Token } from '../lab/shiori/script'
import { focusFor, LONG_ANSWER } from '../lab/presence/subtitles'

const MODES: Record<ShioriMode, Mode> = { Brainstorm: 'brainstorm', Decide: 'decide', Organise: 'organise' }

export function modeOf(mode: ShioriMode): Mode {
  return MODES[mode]
}

export function apiMode(mode: Mode): ShioriMode {
  return (Object.keys(MODES) as ShioriMode[]).find((m) => MODES[m] === mode) ?? 'Brainstorm'
}

/** The whiteboard as the board drawer draws it, done items included. */
export function boardItems(board: WhiteboardItem[]): (BoardItem & { done: boolean; note: string | null })[] {
  return board.map((i) => ({
    id: i.id,
    kind: i.kind.toLowerCase() as BoardKind,
    text: i.text,
    office: i.office ?? undefined,
    done: i.done,
    note: i.note,
  }))
}

/** What the subtitles under Shiori show, from the saved transcript and the answer being written. */
export interface LiveSubtitles {
  /** The last thing the person said. */
  you: string | null
  /** Shiori is reading and has written nothing yet. */
  thinking: boolean
  /** Her answer to it, whole or so far; null before she starts. */
  answer: { tokens: Token[]; streaming: boolean } | null
  /** Why the answer to it failed. */
  failed: string | null
  /** Long enough to be a card instead of a subtitle. */
  long: boolean
  /** Notes she cites in it, for the HUD. */
  notes: string[]
}

/** The full text of her answer to the person's last message, and that message's moment. */
export function answerText(transcript: TranscriptEntry[], draft: string, answering: boolean): { text: string; turn: string | null } {
  let last = -1
  for (let i = transcript.length - 1; i >= 0; i--) {
    if (transcript[i].role === 'Person') {
      last = i
      break
    }
  }
  const saved = transcript.slice(last + 1).find((e) => e.role === 'Shiori')
  return { text: answering && draft ? draft : (saved?.text ?? ''), turn: last >= 0 ? `${transcript[last].at}` : null }
}

/** `shown` cuts the answer to what has been revealed so far (all of it by default). */
export function liveSubtitles(
  transcript: TranscriptEntry[],
  draft: string,
  answering: boolean,
  error: string | null = null,
  shown = Number.POSITIVE_INFINITY,
): LiveSubtitles {
  let last = -1
  for (let i = transcript.length - 1; i >= 0; i--) {
    if (transcript[i].role === 'Person') {
      last = i
      break
    }
  }
  const after = transcript.slice(last + 1)
  const saved = after.find((e) => e.role === 'Shiori')
  const failedEntry = after.find((e) => e.role === 'Failed')
  const whole = answering && draft ? draft : (saved?.text ?? '')
  const text = whole.slice(0, shown)
  const tokens = text ? tokenize(text) : []
  const failed = failedEntry?.text ?? (!answering && !saved && last >= 0 ? error : null)
  const notes = [...new Set(tokens.flatMap((t) => (t.type === 'cite' ? [t.path] : [])))].slice(-3)
  return {
    you: last >= 0 ? transcript[last].text : null,
    thinking: answering && !draft,
    answer: tokens.length > 0 ? { tokens, streaming: (answering && !!draft) || text.length < whole.length } : null,
    failed,
    long: whole.length > LONG_ANSWER,
    notes,
  }
}

/** What Shiori's face shows, from what the page knows now. */
export interface FaceInput {
  /** The person has something in the composer and is in it. */
  typing: boolean
  /** A message was sent and the server has not said yet that she is answering. */
  sending: boolean
  answering: boolean
  /** Part of the answer has arrived. */
  streaming: boolean
  /** The last answer failed. */
  failed: boolean
  ended: boolean
}

export function faceFor(input: FaceInput): FaceState {
  if (input.ended) return 'idle'
  if (input.answering || input.sending) return input.streaming ? 'speaking' : 'thinking'
  if (input.typing) return 'listening'
  if (input.failed) return 'unsure'
  return 'idle'
}

/** Open sessions first, then the ended ones; the most recently changed first in each. */
export function orderSessions(list: ShioriSessionSummary[]): ShioriSessionSummary[] {
  return [...list].sort((a, b) => {
    const ended = Number(a.endedAt !== null) - Number(b.endedAt !== null)
    return ended !== 0 ? ended : Date.parse(b.updatedAt) - Date.parse(a.updatedAt)
  })
}

/** The session to show when none was picked, or the one picked is gone: the latest open one. */
export function pickSession(list: ShioriSessionSummary[], wanted: string): string | undefined {
  if (list.some((s) => s.id === wanted)) return wanted
  return orderSessions(list).find((s) => s.endedAt === null)?.id
}

/** A new session is named after its first message, cut at a word. */
export function titleFrom(text: string, max = 60): string {
  const line = text.replace(/\s+/g, ' ').trim()
  if (line.length <= max) return line
  const cut = line.slice(0, max)
  const space = cut.lastIndexOf(' ')
  return `${(space > max / 2 ? cut.slice(0, space) : cut).replace(/[\s,.;:]+$/, '')}…`
}

/** What the person can do about a failure, when the message names it. */
export function adviceFor(error: string): { label: string; settings: 'models' | 'engines' } | null {
  if (/Settings › Models/i.test(error)) return { label: 'Open Settings › Models', settings: 'models' }
  if (/Claude Code/i.test(error) && /not available|not installed|sign/i.test(error)) return { label: 'Open Settings › Engines', settings: 'engines' }
  return null
}

/** Where Shiori stands and where her subtitles start, in a stage of any height. */
export interface StageLayout {
  /** Centre (fractions of the stage) and radius in px, as the body takes it. */
  focus: { x: number; y: number; size: number }
  /** The subtitles' top, in px from the stage's top. */
  subsTop: number
}

/**
 * Fits Shiori between the stage's header and its dock, leaving the subtitles the room they need
 * (more for a long answer): as big as the prototype makes her when there is room, smaller and
 * centred in what is left when the page is short.
 */
export function stageLayout(w: number, h: number, header: number, dock: number, long: boolean): StageLayout {
  const base = focusFor(w, h, long)
  const below = long ? 1.05 : 1.12
  const region = Math.max(0, h - header - dock)
  const need = long ? Math.max(200, region * 0.42) : 150
  const free = Math.max(0, region - need - 26)
  const size = Math.max(36, Math.min(base.size, free / (1 + below)))
  const centre = header + 8 + Math.max(0, (free - size * (1 + below)) / 2) + size
  return {
    focus: { x: base.x, y: h > 0 ? centre / h : base.y, size: Math.round(size) },
    subsTop: Math.round(centre + size * below + 18),
  }
}

/** The last part of a note's path, without the extension: how a citation reads. */
export function noteName(path: string): string {
  return (path.split('/').pop() ?? path).replace(/\.md$/i, '')
}
