import { describe, expect, it } from 'vitest'
import type { DeskActivity, SoundRule } from '../api/types'
import { BUILTINS, cueOfActivity, DEFAULT_SOUND, resolveSound, SOUND_EVENTS, soundLabel } from './rules'

describe('resolveSound', () => {
  const config: { rules: SoundRule[] } = {
    rules: [
      { event: 'review', roomId: null, deskId: null, sound: 'builtin:coin' },
      { event: 'review', roomId: 'web', deskId: null, sound: 'silent' },
      { event: 'review', roomId: null, deskId: 'stats', sound: 'file:save.ogg' },
    ],
  }

  it('lets a desk win over its room, and its room over the general rule', () => {
    expect(resolveSound(config, 'review', 'stats', 'web')).toBe('file:save.ogg')
    expect(resolveSound(config, 'review', 'frontend', 'web')).toBe('silent')
    expect(resolveSound(config, 'review', 'core', 'core-room')).toBe('builtin:coin')
  })

  it('falls back to the default of the moment', () => {
    expect(resolveSound(config, 'failed', 'stats', 'web')).toBe(DEFAULT_SOUND.failed)
    expect(resolveSound(undefined, 'question')).toBe(DEFAULT_SOUND.question)
  })

  it('names sounds in words', () => {
    expect([soundLabel('builtin:levelUp'), soundLabel('file:save.ogg'), soundLabel('silent')]).toEqual(['Level up', 'save.ogg', 'Silent'])
  })
})

describe('cueOfActivity', () => {
  const waiting = (question: boolean): DeskActivity => ({
    officeId: 'e-corp', deskId: 'stats', taskId: 't1', kind: 'Waiting', step: null, doing: 'Close the app', permissionId: 'per_1',
    at: '2015-06-24T09:00:00Z', headline: 'Close the app', question,
  })

  it('tells a question from a permission, keyed by the request so it plays once', () => {
    expect(cueOfActivity(waiting(true))).toMatchObject({ event: 'question', deskId: 'stats', key: 'per_1' })
    expect(cueOfActivity(waiting(false))?.event).toBe('needs-you')
    expect(cueOfActivity({ ...waiting(false), kind: 'Working', permissionId: null })).toBeNull()
  })
})

describe('the moments', () => {
  it('each has a label and a default that exists, and losing the connection is quiet unless chosen', () => {
    expect(Object.keys(DEFAULT_SOUND).sort()).toEqual(SOUND_EVENTS.map((e) => e.id).sort())
    for (const sound of Object.values(DEFAULT_SOUND)) {
      expect(sound === 'silent' || sound.slice(8) in BUILTINS).toBe(true)
    }
    expect(DEFAULT_SOUND.offline).toBe('silent')
  })
})
