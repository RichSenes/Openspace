import { describe, expect, it } from 'vitest'
import type { TaskStatus, WorkTaskView } from '../api/types'
import { actionsFor, COLUMNS, filterByEpic, groupByColumn } from './taskRules'

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
  notes: [],
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
