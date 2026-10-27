import { describe, expect, it } from 'vitest'
import type { OfficeView, RoutineView } from '../api/types'
import { routineBubble, routineStateOf, withRoutines } from './routines'

const routine = (over: Partial<RoutineView> = {}): RoutineView => ({
  id: 'r1',
  officeId: 'o1',
  name: 'Paper',
  kind: 'morning-paper',
  kindName: 'Morning paper',
  schedule: { days: [], at: '06:00', everyWeeks: 1 },
  settings: '{}',
  enabled: true,
  askFirst: false,
  lastRunAt: null,
  nextRunAt: null,
  lastRun: null,
  ...over,
})

const run = (status: 'Running' | 'Succeeded' | 'Failed') => ({ id: 'x', routineId: 'r1', status, byHand: false, startedAt: '', finishedAt: null, summary: null, log: '' })

describe('routines in the building', () => {
  it('seats the routines as karakuri in the routines room only', () => {
    const office = {
      id: 'o1',
      rooms: [
        { id: 'w', kind: 'Work', desks: [] },
        { id: 'r', kind: 'Routines', desks: [] },
      ],
    } as unknown as OfficeView

    const shown = withRoutines(office, [routine()])

    expect(shown.rooms[0].desks).toEqual([])
    expect(shown.rooms[1].desks.map((d) => [d.id, d.look?.costume])).toEqual([['r1', 'karakuri']])
    expect(withRoutines(office, [])).toBe(office)
  })

  it('sleeps between runs, works while running, waves for proposals, frets after a failure', () => {
    expect(routineStateOf(routine(), false, 0)).toBe('queued')
    expect(routineStateOf(routine({ enabled: false }), false, 0)).toBe('idle')
    expect(routineStateOf(routine(), true, 0)).toBe('working')
    expect(routineStateOf(routine({ lastRun: run('Running') }), false, 0)).toBe('working')
    expect(routineStateOf(routine(), false, 2)).toBe('waiting')
    expect(routineStateOf(routine({ lastRun: run('Failed') }), false, 0)).toBe('failed')
  })

  it('says its current step while it runs, or how many proposals wait', () => {
    expect(routineBubble(routine(), 'Judged 40 of 120 items.', 0)?.headline).toBe('Judged 40 of 120 items.')
    expect(routineBubble(routine(), undefined, 2)).toMatchObject({ kind: 'Waiting', headline: '2 proposals wait for you' })
    expect(routineBubble(routine(), undefined, 0)).toBeUndefined()
  })
})
