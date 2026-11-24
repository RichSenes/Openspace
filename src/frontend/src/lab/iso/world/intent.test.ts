import { describe, expect, it } from 'vitest'
import type { WorldView } from '../../../api/types'
import { intentOf, lookOf, queueOf } from './intent'

describe('what a real character does', () => {
  it('works at its seat, queues at Shiori when it waits or failed, lives its life when free, is away when off', () => {
    expect(intentOf('Working')).toEqual({ where: 'seat', pose: 'type' })
    expect(intentOf('Waiting', 'Question')).toEqual({ where: 'queue', pose: 'notes', bubble: 'question' })
    expect(intentOf('Waiting', 'TaskToReview')).toEqual({ where: 'queue', pose: 'notes', bubble: 'review' })
    expect(intentOf('Waiting', 'TaskToApprove')).toEqual({ where: 'queue', pose: 'notes', bubble: 'question' })
    expect(intentOf('Failed', 'TaskFailed')).toEqual({ where: 'queue', pose: 'stand', bubble: 'failed' })
    expect(intentOf('Free')).toEqual({ where: 'social' })
    expect(intentOf('Off')).toEqual({ where: 'away' })
  })

  it('queues the oldest need first, each character once, only those in the lab', () => {
    const view: WorldView = {
      offices: [],
      waiting: [
        { kind: 'TaskToReview', officeId: 'o', characterId: 'b', id: '2', title: '', detail: '', since: '2026-11-24T10:05:00Z' },
        { kind: 'Question', officeId: 'o', characterId: 'a', id: '1', title: '', detail: '', since: '2026-11-24T10:00:00Z' },
        { kind: 'TaskFailed', officeId: 'o', characterId: 'a', id: '3', title: '', detail: '', since: '2026-11-24T10:09:00Z' },
        { kind: 'DecisionToAccept', officeId: 'o', characterId: null, id: '4', title: '', detail: '', since: '2026-11-24T09:00:00Z' },
        { kind: 'Question', officeId: 'o', characterId: 'gone', id: '5', title: '', detail: '', since: '2026-11-24T09:30:00Z' },
      ],
    }

    expect(queueOf(view, new Set(['a', 'b']))).toEqual([
      { id: 'a', need: 'Question' },
      { id: 'b', need: 'TaskToReview' },
    ])
  })

  it('dresses desks, interns and routines differently, the same way every time', () => {
    const desk = lookOf('d1', 'Desk', '#5b9bff')
    const intern = lookOf('i1', 'Intern', '#5b9bff')
    const routine = lookOf('r1', 'Routine', '#5b9bff')
    expect(desk.coat).toBeUndefined()
    expect(new Set([desk.coat, intern.coat, routine.coat]).size).toBe(3)
    expect(lookOf('d1', 'Desk', '#5b9bff')).toEqual(desk)
    expect(desk.shirt).toBe('#5b9bff')
  })
})
