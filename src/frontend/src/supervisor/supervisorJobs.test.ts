import { describe, expect, it } from 'vitest'
import type { SupervisorJob } from '../api/types'
import { jobsOf, justEnded, outcomeOf, runningFor } from './supervisorJobs'

const job = (over: Partial<SupervisorJob>): SupervisorJob => ({
  id: 'j1',
  officeId: 'o1',
  kind: 'plan',
  what: 'Add a contact page',
  key: 'plan:o1:1',
  startedAt: '2015-06-24T09:00:00Z',
  state: 'Planning',
  tasks: 0,
  epicId: null,
  error: null,
  endedAt: null,
  ...over,
})

describe('supervisor jobs', () => {
  it('lists an office running plans first, then the newest finished', () => {
    const list = [
      job({ id: 'old', state: 'Done', startedAt: '2015-06-24T08:00:00Z' }),
      job({ id: 'new', state: 'Done', startedAt: '2015-06-24T08:30:00Z' }),
      job({ id: 'run', state: 'Planning', startedAt: '2015-06-24T07:00:00Z' }),
      job({ id: 'other', officeId: 'o2' }),
    ]
    expect(jobsOf(list, 'o1').map((j) => j.id)).toEqual(['run', 'new', 'old'])
  })

  it('finds the plan running for a key', () => {
    expect(runningFor([job({ key: 'security:o1:1' })], 'security:o1:1')?.id).toBe('j1')
    expect(runningFor([job({ key: 'security:o1:1', state: 'Done' })], 'security:o1:1')).toBeUndefined()
  })

  it('says what a plan made', () => {
    expect(outcomeOf(job({ state: 'Done', tasks: 3, epicId: 'e1' }))).toBe('3 tasks proposed at the desks')
    expect(outcomeOf(job({ state: 'Done', tasks: 1 }))).toBe('1 task proposed without a desk: choose one for each')
    expect(outcomeOf(job({ state: 'Failed', error: 'No desks yet.' }))).toBe('Could not plan: No desks yet.')
  })

  it('tells which plans ended since the last look', () => {
    const before = new Map<string, SupervisorJob['state']>([['a', 'Planning'], ['b', 'Done']])
    const now = [job({ id: 'a', state: 'Done' }), job({ id: 'b', state: 'Done' }), job({ id: 'c', state: 'Failed' })]
    expect(justEnded(before, now).map((j) => j.id)).toEqual(['a'])
  })
})
