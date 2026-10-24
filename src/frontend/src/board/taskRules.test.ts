import { describe, expect, it } from 'vitest'
import type { TaskStatus, WorkTaskView } from '../api/types'
import { actionsFor, awaitingMerge, checksDone, searchTasks, withoutMerged, COLUMNS, compactTokens, filterByEpic, groupByColumn, reviewVerdictOf, timeOf, totalTokens } from './taskRules'

const task = (title: string, status: TaskStatus): WorkTaskView => ({
  id: title,
  officeId: 'e-corp',
  deskId: null,
  title,
  description: '',
  requestedBy: 'elliot',
  status,
  createdAt: '2015-06-24T09:00:00Z',
  branch: null,
  mergedInto: null,
  epicId: null,
  procedure: [],
  notes: [],
  statusChangedAt: '2015-06-24T09:00:00Z',
  round: 1,
  manualChecks: [],
  usage: [],
})

describe('groupByColumn', () => {
  it('creates every column even when empty', () => {
    const groups = groupByColumn([])
    expect([...groups.keys()]).toEqual(COLUMNS.map((c) => c.status))
    expect([...groups.values()].every((g) => g.length === 0)).toBe(true)
  })

  it('keeps the incoming order inside a column', () => {
    const groups = groupByColumn([task('first', 'Queued'), task('other', 'Review'), task('second', 'Queued')])
    expect(groups.get('Queued')?.map((t) => t.title)).toEqual(['first', 'second'])
  })

  it('leaves discarded tasks out of the board', () => {
    const groups = groupByColumn([task('dropped', 'Discarded')])
    expect([...groups.values()].flat()).toHaveLength(0)
  })
})

describe('actionsFor', () => {
  it('lets you approve only proposed tasks', () => {
    expect(actionsFor('Proposed')).toContain('approve')
    expect(actionsFor('Queued')).not.toContain('approve')
  })

  it('offers review actions only in review', () => {
    expect(actionsFor('Review')).toEqual(['accept', 'send-back', 'discard'])
  })

  it('offers nothing on closed tasks', () => {
    expect(actionsFor('Done')).toEqual([])
    expect(actionsFor('Discarded')).toEqual([])
  })
})

describe('filterByEpic', () => {
  const inEpic = { ...task('form', 'Proposed'), epicId: 'contact' }
  const loose = task('logout', 'Queued')

  it('keeps everything, one epic, or what has no epic', () => {
    expect(filterByEpic([inEpic, loose], 'all')).toHaveLength(2)
    expect(filterByEpic([inEpic, loose], 'contact').map((t) => t.title)).toEqual(['form'])
    expect(filterByEpic([inEpic, loose], 'none').map((t) => t.title)).toEqual(['logout'])
  })
})

describe('reviewVerdictOf', () => {
  const note = (authorKind: 'Desk' | 'Reviewer' | 'System', text: string) => ({ authorKind, authorName: null, deskId: null, text, writtenAt: '2015-06-24T09:00:00Z' })

  it('reads the latest review of work waiting for you', () => {
    const reviewed = { ...task('Logout', 'Review'), notes: [note('Desk', 'Added it.'), note('Reviewer', 'Changes suggested. Untested.')] }
    expect(reviewVerdictOf(reviewed)).toBe('changes')
    expect(reviewVerdictOf({ ...reviewed, notes: [...reviewed.notes, note('Reviewer', 'OK: ready to accept.')] })).toBe('ok')
  })

  it('says nothing without a review, or once the work left review', () => {
    expect(reviewVerdictOf({ ...task('Logout', 'Review'), notes: [note('System', 'OK then')] })).toBeUndefined()
    expect(reviewVerdictOf({ ...task('Logout', 'Done'), notes: [note('Reviewer', 'OK: ready to accept.')] })).toBeUndefined()
  })
})

describe('reviewVerdictOf across rounds', () => {
  const at = (authorKind: 'Desk' | 'Reviewer' | 'System', text: string, writtenAt: string) => ({ authorKind, authorName: null, deskId: null, text, writtenAt })
  const resubmitted = {
    ...task('Logout', 'Review'),
    statusChangedAt: '2015-06-24T10:00:00.1234567+00:00',
    notes: [at('Reviewer', 'Changes suggested. Untested.', '2015-06-24T09:30:00Z'), at('Desk', 'Added the test.', '2015-06-24T10:00:00Z')],
  }
  const now = timeOf('2015-06-24T10:05:00Z')

  it('forgets the review of work since sent back and says a new one is under way', () => {
    expect(reviewVerdictOf(resubmitted, true, now)).toBe('reviewing')
    expect(reviewVerdictOf(resubmitted, false, now)).toBeUndefined()
  })

  it('reads the new review once it comes', () => {
    const reviewed = { ...resubmitted, notes: [...resubmitted.notes, at('Reviewer', 'OK: ready to accept.', '2015-06-24T10:03:00.9999999Z')] }
    expect(reviewVerdictOf(reviewed, true, now)).toBe('ok')
  })

  it('stops waiting when the review failed or never came', () => {
    const failed = { ...resubmitted, notes: [...resubmitted.notes, at('System', 'The review by Claude could not run (limit).', '2015-06-24T10:01:00Z')] }
    expect(reviewVerdictOf(failed, true, now)).toBeUndefined()
    expect(reviewVerdictOf(resubmitted, true, timeOf('2015-06-24T11:00:00Z'))).toBeUndefined()
  })
})

describe('checks and tokens', () => {
  it('counts the checks tried by hand', () => {
    const checks = [
      { number: 1, what: 'Logout works', how: '', checkedAt: '2015-06-24T10:00:00Z' },
      { number: 2, what: 'Menu opens', how: '', checkedAt: null },
    ]
    expect(checksDone({ ...task('Logout', 'Review'), manualChecks: checks })).toEqual({ done: 1, total: 2 })
  })

  it('writes token counts short and adds rounds up', () => {
    expect([compactTokens(950), compactTokens(1234), compactTokens(12_400), compactTokens(1_300_000)]).toEqual(['950', '1.2k', '12k', '1.3M'])
    const total = totalTokens([
      { round: 1, input: 10, output: 5, cacheRead: 100, cacheWrite: 1 },
      { round: 2, input: 1, output: 1, cacheRead: 0, cacheWrite: 0 },
    ])
    expect([total.input, total.output, total.cacheRead, total.cacheWrite]).toEqual([11, 6, 100, 1])
  })
})

describe('awaitingMerge', () => {
  it('lists accepted work on a branch not merged yet, first accepted first', () => {
    const late = { ...task('Late', 'Done'), branch: 'openspace/late', statusChangedAt: '2015-06-24T11:00:00.1234567Z' }
    const early = { ...task('Early', 'Done'), branch: 'openspace/early', statusChangedAt: '2015-06-24T10:00:00Z' }
    const merged = { ...task('Merged', 'Done'), branch: 'openspace/merged', mergedInto: 'main' }
    const review = { ...task('Review', 'Review'), branch: 'openspace/review' }
    const byHand = task('By hand', 'Done')

    expect(awaitingMerge([late, merged, review, byHand, early]).map((t) => t.title)).toEqual(['Early', 'Late'])
  })
})

describe('searchTasks', () => {
  const logout = { ...task('Add a logout button', 'Done'), description: 'In the HEADER of every page' }
  const prices = task('Round prices', 'Queued')

  it('finds every word in the title or the details, in any case', () => {
    expect(searchTasks([logout, prices], 'header LOGOUT').map((t) => t.title)).toEqual(['Add a logout button'])
    expect(searchTasks([logout, prices], 'header prices')).toEqual([])
  })

  it('keeps everything for an empty search', () => {
    expect(searchTasks([logout, prices], '   ')).toHaveLength(2)
  })
})

describe('withoutMerged', () => {
  it('leaves out done work already merged and counts it', () => {
    const merged = { ...task('Merged', 'Done'), mergedInto: 'main' }
    const waiting = { ...task('Waiting', 'Done'), branch: 'openspace/waiting' }
    const result = withoutMerged([merged, waiting, task('Running', 'Running')])
    expect(result.shown.map((t) => t.title)).toEqual(['Waiting', 'Running'])
    expect(result.hidden).toBe(1)
  })
})
