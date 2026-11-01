import { describe, expect, it } from 'vitest'
import type { VersionView } from '../api/types'
import { noteLines, readSnooze, releaseToTell, snoozed } from './release'

const now = new Date('2026-03-02T08:00:00Z')
const view = (over: Partial<VersionView> = {}): VersionView => ({
  current: '0.9.0',
  latest: { version: '0.10.0', name: 'Openspace 0.10.0', notes: '', url: 'https://example.com/r', publishedAt: null },
  newer: true,
  checkedAt: null,
  ...over,
})

describe('the new version notice', () => {
  it('tells about a newer release only', () => {
    expect(releaseToTell(view(), null, now)?.version).toBe('0.10.0')
    expect(releaseToTell(view({ newer: false }), null, now)).toBeUndefined()
    expect(releaseToTell(undefined, null, now)).toBeUndefined()
  })

  it('waits a day after "remind me tomorrow", and a newer release comes at once', () => {
    const snooze = snoozed(view().latest!, now)
    expect(releaseToTell(view(), snooze, new Date('2026-03-03T07:59:00Z'))).toBeUndefined()
    expect(releaseToTell(view(), snooze, new Date('2026-03-03T08:01:00Z'))?.version).toBe('0.10.0')
    const newer = view({ latest: { ...view().latest!, version: '0.11.0' } })
    expect(releaseToTell(newer, snooze, now)?.version).toBe('0.11.0')
  })

  it('ignores a stored snooze it cannot read', () => {
    expect(readSnooze('not json')).toBeNull()
    expect(readSnooze('{"version":1}')).toBeNull()
    expect(readSnooze(JSON.stringify(snoozed(view().latest!, now)))).toEqual({ version: '0.10.0', until: '2026-03-03T08:00:00.000Z' })
  })

  it('turns the notes into plain lines, marks and links taken out', () => {
    expect(noteLines("## What's new\n\n- **Rooms** can be [renamed](https://example.com)\n* `Fast` merges\nThanks!")).toEqual([
      { kind: 'heading', text: "What's new" },
      { kind: 'item', text: 'Rooms can be renamed' },
      { kind: 'item', text: 'Fast merges' },
      { kind: 'text', text: 'Thanks!' },
    ])
  })
})
