import { describe, expect, it } from 'vitest'
import type { ShioriSessionView, TranscriptEntry } from '../api/types'
import { applyEvent, liveReducer, NO_LIVE, sessionOf, withEntry, type Live } from './live'

const entry = (role: TranscriptEntry['role'], text: string, at = '2026-01-01T10:00:00Z'): TranscriptEntry => ({ role, text, mode: 'Brainstorm', at })

const view: ShioriSessionView = {
  id: 's1',
  title: 'Made-up session',
  mode: 'Brainstorm',
  note: 'Sessions/made-up.md',
  transcript: [],
  board: [],
  createdAt: '2026-01-01T09:00:00Z',
  updatedAt: '2026-01-01T09:00:00Z',
  endedAt: null,
  answering: false,
}

const state = (answering: boolean, error: string | null = null, ended = false) =>
  ({ type: 'state', state: { sessionId: 's1', mode: 'Decide', answering, ended, error } }) as const

describe('liveReducer', () => {
  it('assembles the answer from its pieces in order', () => {
    let live: Live = NO_LIVE
    for (const chunk of ['The ', 'merge ', 'order [[Plans/x]]']) live = liveReducer(live, { type: 'text', sessionId: 's1', chunk })
    expect(live.draft).toBe('The merge order [[Plans/x]]')
  })

  it('drops the draft once the whole answer is saved', () => {
    const live = liveReducer({ draft: 'half', error: null }, { type: 'message', sessionId: 's1', entry: entry('Shiori', 'whole') })
    expect(live.draft).toBe('')
  })

  it('keeps the failure the state reports, and forgets it on the next question', () => {
    const failed = liveReducer({ draft: 'half', error: null }, state(false, 'Engine missing'))
    expect(failed).toEqual({ draft: '', error: 'Engine missing' })
    expect(liveReducer(failed, { type: 'message', sessionId: 's1', entry: entry('Person', 'again') }).error).toBeNull()
    expect(liveReducer(failed, state(true)).error).toBeNull()
  })

  it('ignores a mode line and the board', () => {
    const live = { draft: 'x', error: null }
    expect(liveReducer(live, { type: 'message', sessionId: 's1', entry: entry('Mode', 'Decide') })).toBe(live)
    expect(liveReducer(live, { type: 'board', sessionId: 's1', board: [] })).toBe(live)
  })
})

describe('applyEvent', () => {
  it('adds a line once even when heard twice', () => {
    const e = entry('Person', 'hello')
    const once = applyEvent(view, { type: 'message', sessionId: 's1', entry: e })
    const twice = applyEvent(once, { type: 'message', sessionId: 's1', entry: e })
    expect(twice.transcript).toHaveLength(1)
    expect(withEntry(twice.transcript, entry('Person', 'hello', '2026-01-01T10:01:00Z'))).toHaveLength(2)
  })

  it('follows the state: mode, answering and the end', () => {
    const now = new Date('2026-01-01T11:00:00Z')
    expect(applyEvent(view, state(true), now)).toMatchObject({ mode: 'Decide', answering: true, endedAt: null })
    expect(applyEvent(view, state(false, null, true), now).endedAt).toBe(now.toISOString())
  })

  it('takes the whole board and counts a first piece of text as answering', () => {
    const board = [{ id: 'w1', kind: 'Idea' as const, text: 'An idea', done: false, office: null, note: null }]
    expect(applyEvent(view, { type: 'board', sessionId: 's1', board }).board).toBe(board)
    expect(applyEvent(view, { type: 'text', sessionId: 's1', chunk: 'a' }).answering).toBe(true)
  })

  it('knows which session an event is about', () => {
    expect(sessionOf(state(true))).toBe('s1')
    expect(sessionOf({ type: 'text', sessionId: 's2', chunk: '' })).toBe('s2')
  })
})
