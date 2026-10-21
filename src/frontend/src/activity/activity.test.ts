import { describe, expect, it } from 'vitest'
import type { DeskActivity } from '../api/types'
import { activityByDesk, isActive, mergeActivity, waitingDesks } from './activity'

const activity = (deskId: string, headline: string, kind: DeskActivity['kind'] = 'Working', officeId = 'e-corp'): DeskActivity => ({
  officeId,
  deskId,
  taskId: 't1',
  kind,
  step: headline,
  doing: null,
  permissionId: null,
  at: '2015-06-24T09:00:00Z',
  headline,
})

describe('mergeActivity', () => {
  it('replaces the previous activity of the same desk', () => {
    const merged = mergeActivity([activity('frontend', 'Reading'), activity('tester', 'Testing')], activity('frontend', 'Editing'))
    expect(merged.map((a) => a.headline)).toEqual(['Testing', 'Editing'])
  })

  it('starts a list when there is none', () => {
    expect(mergeActivity(undefined, activity('frontend', 'Reading'))).toHaveLength(1)
  })
})

describe('activityByDesk', () => {
  it('keeps only the office asked for', () => {
    const map = activityByDesk([activity('frontend', 'A'), activity('auditor', 'B', 'Working', 'allsafe')], 'e-corp')
    expect([...map.keys()]).toEqual(['frontend'])
  })
})

describe('isActive', () => {
  it('lights the screen while working or waiting, not after stopping', () => {
    expect(isActive(activity('d', 'x', 'Working'))).toBe(true)
    expect(isActive(activity('d', 'x', 'Waiting'))).toBe(true)
    expect(isActive(activity('d', 'x', 'Stopped'))).toBe(false)
    expect(isActive(undefined)).toBe(false)
  })
})

describe('waitingDesks', () => {
  it('lists only waiting desks, named after their office and desk', () => {
    const offices = [
      { id: 'e-corp', name: 'E Corp', repositoryPath: null, rooms: [{ id: 'web', name: 'Web', context: '', desks: [{ id: 'frontend', name: 'Frontend', agentName: 'build', model: null, roleId: null }] }] },
    ]
    const list = [activity('frontend', 'Wants to bash ls -la', 'Waiting'), activity('tester', 'Testing', 'Working')]

    expect(waitingDesks(list, offices)).toEqual([
      { taskId: 't1', officeName: 'E Corp', deskName: 'Frontend', request: 'Wants to bash ls -la' },
    ])
  })
})
