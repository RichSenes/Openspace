import { describe, expect, it } from 'vitest'
import type { DeskContext } from '../api/types'
import { contextByDesk, energyOf, energyWords, mergeContext } from './energy'

const context = (over: Partial<DeskContext> = {}): DeskContext => ({
  officeId: 'office-1',
  deskId: 'desk-1',
  taskId: 'task-1',
  sessionId: 'ses_1',
  used: 50_000,
  window: 200_000,
  compactions: 0,
  at: '2015-06-24T09:00:00Z',
  refreshedAt: null,
  left: 0.75,
  ...over,
})

describe('desk energy', () => {
  it('keeps the latest energy of each desk, per office', () => {
    const list = mergeContext(mergeContext([context()], context({ used: 60_000, left: 0.7 })), context({ deskId: 'desk-2', officeId: 'office-2' }))
    expect(list).toHaveLength(2)
    expect(contextByDesk(list, 'office-1').get('desk-1')?.used).toBe(60_000)
    expect(contextByDesk(list, 'office-1').has('desk-2')).toBe(false)
  })

  it('shows no battery on a resting desk or when the window is unknown', () => {
    expect(energyOf(context(), 'idle', 0)).toBeUndefined()
    expect(energyOf(context(), 'queued', 0)).toBeUndefined()
    expect(energyOf(context({ left: null }), 'working', 0)).toBeUndefined()
    expect(energyOf(undefined, 'working', 0)).toBeUndefined()
    expect(energyOf(context(), 'done', 0)).toEqual({ left: 0.75 })
  })

  it('says how long ago the agent got its energy back', () => {
    const refreshedAt = '2015-06-24T09:00:00Z'
    expect(energyOf(context({ refreshedAt, left: 1 }), 'working', Date.parse(refreshedAt) + 2500)).toEqual({ left: 1, sinceRefresh: 2.5 })
  })

  it('puts the energy in words for the agent card', () => {
    expect(energyWords(context())).toBe('75% of its conversation left')
    expect(energyWords(context({ compactions: 1 }))).toBe('75% of its conversation left · summarised once')
    expect(energyWords(context({ compactions: 3 }))).toBe('75% of its conversation left · summarised 3 times')
    expect(energyWords(context({ left: null }))).toBeNull()
  })
})
