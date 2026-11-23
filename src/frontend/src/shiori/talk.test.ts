import { describe, expect, it } from 'vitest'
import type { ShioriSessionSummary, TranscriptEntry } from '../api/types'
import { adviceFor, answerText, stageLayout, apiMode, boardItems, faceFor, liveSubtitles, modeOf, noteName, orderSessions, pickSession, titleFrom, type FaceInput } from './talk'

const entry = (role: TranscriptEntry['role'], text: string): TranscriptEntry => ({ role, text, mode: 'Brainstorm', at: '2026-01-01T10:00:00Z' })

const summary = (id: string, updatedAt: string, endedAt: string | null = null): ShioriSessionSummary => ({
  id,
  title: id,
  mode: 'Brainstorm',
  note: `Sessions/${id}.md`,
  messages: 2,
  createdAt: '2026-01-01T09:00:00Z',
  updatedAt,
  endedAt,
  answering: false,
})

describe('liveSubtitles', () => {
  it('shows the last question and the answer that follows it', () => {
    const s = liveSubtitles([entry('Person', 'first'), entry('Shiori', 'old'), entry('Person', 'second'), entry('Shiori', 'See [[Plans/merge-order]] first')], '', false)
    expect(s.you).toBe('second')
    expect(s.answer?.tokens.map((t) => (t.type === 'cite' ? `#${t.path}` : t.text))).toEqual(['See', '#Plans/merge-order', 'first'])
    expect(s.answer?.streaming).toBe(false)
    expect(s.notes).toEqual(['Plans/merge-order'])
  })

  it('thinks until the first piece arrives, then streams the draft', () => {
    const t = [entry('Person', 'q')]
    expect(liveSubtitles(t, '', true)).toMatchObject({ thinking: true, answer: null })
    const s = liveSubtitles(t, 'Half an', true)
    expect(s.thinking).toBe(false)
    expect(s.answer?.streaming).toBe(true)
  })

  it('hides an older answer under a newer question', () => {
    expect(liveSubtitles([entry('Person', 'a'), entry('Shiori', 'b'), entry('Person', 'c')], '', false).answer).toBeNull()
  })

  it('says why an answer failed, from the transcript or the live error', () => {
    expect(liveSubtitles([entry('Person', 'a'), entry('Failed', 'No engine')], '', false).failed).toBe('No engine')
    expect(liveSubtitles([entry('Person', 'a')], '', false, 'Lost').failed).toBe('Lost')
    expect(liveSubtitles([], '', false, 'Lost').failed).toBeNull()
  })

  it('turns a long answer into a card', () => {
    expect(liveSubtitles([entry('Person', 'a'), entry('Shiori', 'word '.repeat(80))], '', false).long).toBe(true)
  })

  it('shows only what has been revealed, still writing, and is long by the whole answer', () => {
    const t = [entry('Person', 'a'), entry('Shiori', 'word '.repeat(80))]
    const s = liveSubtitles(t, '', false, null, 9)
    expect(s.answer?.tokens).toEqual(liveSubtitles([entry('Person', 'a'), entry('Shiori', 'word word')], '', false).answer?.tokens)
    expect(s.answer?.streaming).toBe(true)
    expect(s.long).toBe(true)
    expect(answerText(t, '', false)).toEqual({ text: 'word '.repeat(80), turn: '2026-01-01T10:00:00Z' })
    expect(liveSubtitles([entry('Person', 'a'), entry('Shiori', 'short')], '', false).long).toBe(false)
  })
})

describe('faceFor', () => {
  const base: FaceInput = { typing: false, sending: false, answering: false, streaming: false, failed: false, ended: false }

  it('follows a whole exchange', () => {
    expect(faceFor(base)).toBe('idle')
    expect(faceFor({ ...base, typing: true })).toBe('listening')
    expect(faceFor({ ...base, sending: true })).toBe('thinking')
    expect(faceFor({ ...base, answering: true })).toBe('thinking')
    expect(faceFor({ ...base, answering: true, streaming: true })).toBe('speaking')
  })

  it('does not listen over an answer, doubts after a failure, rests once ended', () => {
    expect(faceFor({ ...base, typing: true, answering: true, streaming: true })).toBe('speaking')
    expect(faceFor({ ...base, failed: true })).toBe('unsure')
    expect(faceFor({ ...base, failed: true, typing: true })).toBe('listening')
    expect(faceFor({ ...base, ended: true, typing: true })).toBe('idle')
  })
})

describe('sessions', () => {
  const list = [summary('old', '2026-01-01T10:00:00Z'), summary('ended', '2026-01-03T10:00:00Z', '2026-01-03T10:00:00Z'), summary('new', '2026-01-02T10:00:00Z')]

  it('lists open sessions first, newest first', () => {
    expect(orderSessions(list).map((s) => s.id)).toEqual(['new', 'old', 'ended'])
  })

  it('keeps the picked session, else opens the latest open one', () => {
    expect(pickSession(list, 'ended')).toBe('ended')
    expect(pickSession(list, 'gone')).toBe('new')
    expect(pickSession([summary('e', '2026-01-01T10:00:00Z', '2026-01-01T10:00:00Z')], '')).toBeUndefined()
  })
})

describe('helpers', () => {
  it('maps modes both ways', () => {
    expect(modeOf('Organise')).toBe('organise')
    expect(apiMode('decide')).toBe('Decide')
  })

  it('names a session after its first message, cut at a word', () => {
    expect(titleFrom('  Merge   order\nafter yesterday ')).toBe('Merge order after yesterday')
    const t = titleFrom('word '.repeat(30), 20)
    expect(t.endsWith('…')).toBe(true)
    expect(t.length).toBeLessThanOrEqual(21)
  })

  it('points to the setting a failure names', () => {
    expect(adviceFor('Shiori talks through Claude Code for now: choose Claude Code for Shiori in Settings › Models.')?.settings).toBe('models')
    expect(adviceFor('Claude Code is not available on this machine, so Shiori cannot answer.')?.settings).toBe('engines')
    expect(adviceFor('Something else')).toBeNull()
  })

  it('reads the board in the lab’s kinds and names notes briefly', () => {
    expect(boardItems([{ id: 'w1', kind: 'Decision', text: 'Ship', done: true, office: 'Crumb', note: 'Decisions/ship.md' }])[0]).toMatchObject({ kind: 'decision', office: 'Crumb', done: true })
    expect(noteName('Plans/merge-order.md')).toBe('merge-order')
  })
})

describe('stageLayout', () => {
  it('keeps her whole size on a tall stage and puts the subtitles under her', () => {
    const tall = stageLayout(1160, 1200, 60, 150, false)
    expect(tall.focus.size).toBe(255)
    expect(tall.subsTop).toBeGreaterThan(tall.focus.y * 1200 + tall.focus.size)
    expect(tall.subsTop).toBeLessThanOrEqual(1200 - 150 - 150)
  })

  it('shrinks her on a short stage so the subtitles keep their room below the header', () => {
    const short = stageLayout(1160, 800, 64, 150, false)
    expect(short.focus.y * 800 - short.focus.size).toBeGreaterThanOrEqual(64)
    expect(800 - 150 - short.subsTop).toBeGreaterThanOrEqual(150)
  })

  it('leaves more room for a long answer', () => {
    const a = stageLayout(390, 700, 110, 170, false)
    const b = stageLayout(390, 700, 110, 170, true)
    expect(b.subsTop).toBeLessThan(a.subsTop)
    expect(b.focus.size).toBeLessThan(a.focus.size)
  })
})
