import type { ShioriSessionView, ShioriState, TranscriptEntry, WhiteboardItem } from '../api/types'

/** What the hub says about Shiori's sessions, as one kind of value. */
export type LiveEvent =
  | { type: 'text'; sessionId: string; chunk: string }
  | { type: 'message'; sessionId: string; entry: TranscriptEntry }
  | { type: 'board'; sessionId: string; board: WhiteboardItem[] }
  | { type: 'state'; state: ShioriState }

export function sessionOf(event: LiveEvent): string {
  return event.type === 'state' ? event.state.sessionId : event.sessionId
}

/** What only the live connection knows about a session: the answer being written and the last failure. */
export interface Live {
  /** The answer so far, while it is written; empty otherwise. */
  draft: string
  /** Why the last answer failed, until the next message. */
  error: string | null
}

export const NO_LIVE: Live = { draft: '', error: null }

export function liveReducer(live: Live, event: LiveEvent): Live {
  switch (event.type) {
    case 'text':
      return { draft: live.draft + event.chunk, error: null }
    case 'message':
      // The whole answer is in the transcript now; a new question clears the old failure.
      if (event.entry.role === 'Shiori' || event.entry.role === 'Failed') return { ...live, draft: '' }
      if (event.entry.role === 'Person') return { draft: '', error: null }
      return live
    case 'board':
      return live
    case 'state':
      if (event.state.answering) return { ...live, error: null }
      return { draft: '', error: event.state.error ?? live.error }
  }
}

function sameEntry(a: TranscriptEntry, b: TranscriptEntry): boolean {
  return a.role === b.role && a.text === b.text && a.at === b.at
}

/** Adds an entry to the transcript once: the page may hear it from the hub and from its own request. */
export function withEntry(transcript: TranscriptEntry[], entry: TranscriptEntry): TranscriptEntry[] {
  return transcript.some((e) => sameEntry(e, entry)) ? transcript : [...transcript, entry]
}

/** The session as the page holds it, after something the hub said. */
export function applyEvent(view: ShioriSessionView, event: LiveEvent, now: Date = new Date()): ShioriSessionView {
  switch (event.type) {
    case 'text':
      return view.answering ? view : { ...view, answering: true }
    case 'message':
      return { ...view, transcript: withEntry(view.transcript, event.entry), updatedAt: event.entry.at }
    case 'board':
      return { ...view, board: event.board }
    case 'state': {
      const s = event.state
      return {
        ...view,
        mode: s.mode,
        answering: s.answering,
        endedAt: s.ended ? (view.endedAt ?? now.toISOString()) : view.endedAt,
      }
    }
  }
}
