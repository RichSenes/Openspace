import { describe, expect, it } from 'vitest'
import type { Entry } from '../shiori/script'
import { tokenize } from '../shiori/script'
import { focusFor, LONG_ANSWER, subtitles } from './subtitles'

const you = (at: number, text: string): Entry => ({ kind: 'user', id: `u${at}`, at, text })
const shiori = (at: number, text: string, over: Partial<Extract<Entry, { kind: 'shiori' }>> = {}): Entry => {
  const tokens = tokenize(text)
  return { kind: 'shiori', id: `s${at}`, at, tokens, shown: tokens.length, thinking: false, streaming: false, reads: [], ...over }
}

describe('subtitles', () => {
  it('show the last question and the answer to it', () => {
    const s = subtitles([you(1, 'first'), shiori(2, 'one'), you(3, 'second'), shiori(4, 'two [[Decisions/x]]')])
    expect(s.you?.text).toBe('second')
    expect(s.shown.map((t) => (t.type === 'word' ? t.text : t.path))).toEqual(['two', 'Decisions/x'])
    expect(s.notes).toEqual(['Decisions/x'])
  })

  it('hide an older answer once a new question is asked', () => {
    const s = subtitles([you(1, 'first'), shiori(2, 'one'), you(3, 'second')])
    expect(s.shiori).toBeNull()
    expect(s.shown).toEqual([])
  })

  it('pin what Shiori reads while she thinks', () => {
    const s = subtitles([you(1, 'q'), shiori(2, 'a [[Rooms/b]]', { thinking: true, shown: 0, reads: ['Decisions/a', 'Rooms/b'] })])
    expect(s.notes).toEqual(['Decisions/a', 'Rooms/b'])
  })

  it('call an answer long past the limit', () => {
    const long = 'word '.repeat(Math.ceil(LONG_ANSWER / 5) + 5)
    expect(subtitles([you(1, 'q'), shiori(2, long)]).long).toBe(true)
    expect(subtitles([you(1, 'q'), shiori(2, 'short')]).long).toBe(false)
  })
})

describe('where Shiori stands', () => {
  it('steps back, smaller and higher, for a long answer', () => {
    const short = focusFor(1400, 900, false)
    const long = focusFor(1400, 900, true)
    expect(long.size).toBeLessThan(short.size)
    expect(long.y).toBeLessThan(short.y)
  })

  it('fits a phone', () => {
    const f = focusFor(390, 844, false)
    expect(f.size * 2).toBeLessThan(390)
  })
})
