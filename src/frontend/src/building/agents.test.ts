import { describe, expect, it } from 'vitest'
import type { DeskActivity, WorkTaskView } from '../api/types'
import { agentStateOf, appearanceOf, deskLookOf, LOOK_PARTS, lookOf, shuffledLook } from './agents'

const task = (status: WorkTaskView['status'], createdAt = '2026-01-01T10:00:00Z', deskId = 'desk-1'): WorkTaskView => ({
  id: `${status}-${createdAt}`,
  officeId: 'e-corp',
  deskId,
  title: 'Patch the login page',
  description: '',
  requestedBy: 'elliot',
  status,
  createdAt,
  branch: null,
  mergedInto: null,
  epicId: null,
  procedure: [],
  notes: [],
  statusChangedAt: createdAt,
  round: 1,
  manualChecks: [],
  usage: [],
})

const activity = (kind: DeskActivity['kind'], doing: string | null = null): DeskActivity => ({
  officeId: 'e-corp',
  deskId: 'desk-1',
  taskId: 't',
  kind,
  step: null,
  doing,
  permissionId: null,
  at: '2026-01-01T10:00:00Z',
  headline: 'Patching the login page',
})

describe('agentStateOf', () => {
  it('is idle with nothing to do', () => {
    expect(agentStateOf('desk-1', [], undefined)).toBe('idle')
  })

  it('waits for a person before anything else', () => {
    expect(agentStateOf('desk-1', [task('Running')], activity('Waiting'))).toBe('waiting')
  })

  it('works, or reads while it only looks at things', () => {
    expect(agentStateOf('desk-1', [task('Running')], activity('Working', 'Editing src/app.ts'))).toBe('working')
    expect(agentStateOf('desk-1', [task('Running')], activity('Working', 'Reading src/app.ts'))).toBe('reading')
    expect(agentStateOf('desk-1', [task('Running')], undefined)).toBe('working')
  })

  it('shows the latest failure, then work to review, then queued work', () => {
    expect(agentStateOf('desk-1', [task('Done', '2026-01-01T09:00:00Z'), task('Failed', '2026-01-01T10:00:00Z')], undefined)).toBe('failed')
    expect(agentStateOf('desk-1', [task('Review'), task('Queued', '2026-01-01T11:00:00Z')], undefined)).toBe('done')
    expect(agentStateOf('desk-1', [task('Queued')], undefined)).toBe('queued')
  })

  it('only looks at its own desk', () => {
    expect(agentStateOf('desk-1', [task('Running', undefined, 'desk-2')], undefined)).toBe('idle')
  })
})

describe('appearanceOf', () => {
  it('always gives a desk the same person', () => {
    expect(appearanceOf('desk-1')).toEqual(appearanceOf('desk-1'))
  })

  it('gives different desks different people', () => {
    const looks = new Set(Array.from({ length: 12 }, (_, i) => JSON.stringify(appearanceOf(`desk-${i}`))))
    expect(looks.size).toBeGreaterThan(6)
  })
})

describe('lookOf', () => {
  it('is the look picked from the id until one is chosen', () => {
    expect(lookOf({ id: 'desk-1' })).toEqual(appearanceOf('desk-1'))
    expect(lookOf({ id: 'desk-1', look: null })).toEqual(appearanceOf('desk-1'))
  })

  it('wears what was chosen, keeping the picked look for parts it does not know', () => {
    const look = lookOf({ id: 'desk-1', look: { skin: 'deep', hair: 'teal', hairStyle: 'bun', cloth: 'purple', outfit: 'coat', accessory: 'monocle', costume: null } })
    expect(look).toMatchObject({ skin: 'deep', hair: appearanceOf('desk-1').hair, hairStyle: 'bun', cloth: 'purple', outfit: 'coat' })
    expect(look.accessory).toBeUndefined()
    expect(look.costume).toBeUndefined()
  })

  it('goes to the API and back unchanged', () => {
    const chosen = { skin: 'tan', hair: 'grey', hairStyle: 'long', cloth: 'mustard', accessory: 'headset', outfit: null, costume: 'skeleton' } as const
    expect(deskLookOf(lookOf({ id: 'x', look: chosen }))).toEqual(chosen)
  })
})

describe('shuffledLook', () => {
  it('only wears known parts, never a costume or the pointy hat', () => {
    for (let seed = 0; seed < 50; seed++) {
      let n = seed
      const look = shuffledLook(() => ((n = (n * 9301 + 49297) % 233280) / 233280))
      expect(LOOK_PARTS.skin).toContain(look.skin)
      expect(LOOK_PARTS.cloth).toContain(look.cloth)
      expect(look.costume).toBeUndefined()
      expect(look.accessory).not.toBe('pointyHat')
    }
  })
})
