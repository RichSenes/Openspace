import { describe, expect, it } from 'vitest'
import { buildTimeline, citationsOf, SESSION, snapshot, timeOf, tokenize, type Step } from './script'

const steps: Step[] = [
  { kind: 'user', text: 'Hello' },
  { kind: 'shiori', text: 'Hi, see [[Decisions/x]].', mood: 'pleased', board: [{ kind: 'idea', text: 'One' }] },
  { kind: 'mode', mode: 'decide' },
]

describe('the scripted session', () => {
  it('splits an answer into words and citations, keeping punctuation glued', () => {
    expect(tokenize('Hi, see [[Decisions/x]].')).toEqual([
      { type: 'word', text: 'Hi,', glued: false },
      { type: 'word', text: 'see', glued: false },
      { type: 'cite', path: 'Decisions/x', glued: false },
      { type: 'word', text: '.', glued: true },
    ])
    expect(citationsOf('a [[A/b]] c [[C/d]]')).toEqual(['A/b', 'C/d'])
  })

  it('types the message in the composer before it is sent, while the face listens', () => {
    const tl = buildTimeline(steps)
    const s = snapshot(tl, tl.steps[0].start + 3 * tl.pace.charMs)
    expect(s.composer).toBe('Hel')
    expect(s.entries).toEqual([])
    expect(s.face).toBe('listening')
  })

  it('thinks, then streams the answer word by word', () => {
    const tl = buildTimeline(steps)
    const answer = tl.steps[1]
    if (answer.kind !== 'shiori') throw new Error('expected an answer')
    const thinking = snapshot(tl, answer.start + 10)
    expect(thinking.face).toBe('thinking')
    expect(thinking.entries.at(-1)).toMatchObject({ kind: 'shiori', thinking: true, shown: 0 })

    const two = snapshot(tl, answer.speakStart + tl.pace.wordMs + 1)
    expect(two.face).toBe('speaking')
    expect(two.entries.at(-1)).toMatchObject({ shown: 2, streaming: true })
    expect(two.links).toEqual([])

    const after = snapshot(tl, answer.speakEnd + 1)
    expect(after.face).toBe('pleased')
    expect(after.links).toEqual(['Decisions/x'])
    expect(after.board).toEqual([])
    expect(snapshot(tl, answer.boardAt[0]).board).toEqual([{ id: 'b1-0', kind: 'idea', text: 'One' }])
  })

  it('writes a mode change into the transcript and switches the mode', () => {
    const tl = buildTimeline(steps)
    expect(snapshot(tl, timeOf(tl, 2) - 1).mode).toBe('brainstorm')
    const s = snapshot(tl, timeOf(tl, 2))
    expect(s.mode).toBe('decide')
    expect(s.entries.at(-1)).toMatchObject({ kind: 'mode', mode: 'decide', by: 'script' })
  })

  it('puts a switch made by hand in its place in time', () => {
    const tl = buildTimeline(steps)
    const t = tl.steps[0].end + 5
    const s = snapshot(tl, t + 1, [{ t, mode: 'organise' }])
    expect(s.mode).toBe('organise')
    expect(s.entries.map((e) => e.kind)).toEqual(['user', 'shiori', 'mode'])
    expect(s.entries.find((e) => e.kind === 'mode')).toMatchObject({ by: 'you', mode: 'organise' })
  })

  it('ends with the whole board and every source', () => {
    const tl = buildTimeline(SESSION)
    const end = snapshot(tl, tl.duration)
    expect(end.done).toBe(true)
    expect(end.mode).toBe('organise')
    expect(end.face).toBe('idle')
    expect(end.board.map((b) => b.kind)).toEqual(['idea', 'idea', 'doubt', 'doubt', 'decision', 'task', 'task', 'task'])
    expect(end.links).toContain('Rooms/Frontend/briefing')
    expect(end.composer).toBe('')
  })

  it('jumps to the middle of an answer', () => {
    const tl = buildTimeline(SESSION)
    const s = snapshot(tl, timeOf(tl, 6, 'mid'))
    expect(s.face).toBe('speaking')
    expect(s.mode).toBe('decide')
  })
})
