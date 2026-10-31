import { describe, expect, it } from 'vitest'
import type { FeedLine } from '../api/types'
import { FEED_MAX, feedKind, feedPlace, feedText, feedTime, moreLines } from './feed'

const line = (over: Partial<FeedLine> = {}): FeedLine => ({
  id: 1,
  at: '2015-06-24T09:30:00',
  kind: 'Change',
  summary: 'Changed header.php',
  succeeded: true,
  taskId: 'task-1',
  taskTitle: 'Add a logout button',
  officeId: 'office-1',
  officeName: 'E Corp',
  roomId: 'room-1',
  roomName: 'Web',
  deskId: 'desk-1',
  deskName: 'Frontend',
  excerpt: null,
  ...over,
})

describe('feed lines', () => {
  it('say who did it and where, with the office when every office is followed', () => {
    expect(feedPlace(line(), false)).toBe('Frontend · Web')
    expect(feedPlace(line(), true)).toBe('E Corp · Frontend · Web')
    expect(feedPlace(line({ deskName: null, roomName: null }), false)).toBe('No desk')
  })

  it('say in words that something failed', () => {
    expect(feedKind(line())).toBe('Change')
    expect(feedKind(line({ kind: 'Command', succeeded: false }))).toBe('Command failed')
    expect(feedKind(line({ kind: 'Lifecycle', succeeded: null }))).toBe('Task')
  })

  it('give the start of a note after what happened', () => {
    expect(feedText(line())).toBe('Changed header.php')
    expect(feedText(line({ kind: 'Note', summary: 'Note from Frontend', excerpt: 'The header is shared.' }))).toBe('Note from Frontend: The header is shared.')
  })

  it('show the hour today and the day before today', () => {
    const today = new Date('2015-06-24T18:00:00')
    expect(feedTime('2015-06-24T09:30:00', today)).not.toMatch(/Jun|giu/i)
    expect(feedTime('2015-06-20T09:30:00', today)).toMatch(/20/)
  })

  it('ask for older lines a page at a time, up to what the server gives', () => {
    expect(moreLines(50)).toBe(100)
    expect(moreLines(180)).toBe(FEED_MAX)
  })
})
