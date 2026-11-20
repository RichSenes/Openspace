import { describe, expect, it } from 'vitest'
import type { LocalAiSettings } from '../api/types'
import { cardLine, changed, draftOf, newModel, settingsOf } from './localAi'

const saved: LocalAiSettings = {
  enabled: true,
  serverUrl: 'http://127.0.0.1:8080/',
  models: [{ id: 'gemma', name: 'Gemma', command: '/opt/llm/gemma.sh', arguments: ['-m', '/m/gemma.gguf'], memoryMiB: 9000, context: 32768 }],
  holders: [{ name: 'Image studio', match: 'studio.py' }],
  idleUnloadMinutes: 10,
  yieldGraceMinutes: 3,
  decider: { command: '', arguments: [], url: 'http://127.0.0.1:8017/' },
}

describe('local AI drafts', () => {
  it('edits the arguments as lines and sends them back as a list', () => {
    const draft = draftOf(saved)
    expect(draft.models[0].arguments).toBe('-m\n/m/gemma.gguf')
    expect(settingsOf(draft)).toEqual(saved)
    expect(changed(saved, draft)).toBe(false)
  })

  it('sees a change in an argument, blank lines aside', () => {
    const draft = draftOf(saved)
    expect(changed(saved, { ...draft, models: [{ ...draft.models[0], arguments: '-m\n\n/m/gemma.gguf\n' }] })).toBe(false)
    expect(changed(saved, { ...draft, models: [{ ...draft.models[0], arguments: '-m\n/m/other.gguf' }] })).toBe(true)
  })

  it('names a new model with a name not taken', () => {
    expect(newModel(['model', 'model-2']).id).toBe('model-3')
  })

  it('tells the graphics memory in GB, when the card can be read', () => {
    expect(cardLine(12288, 6349)).toBe('6.2 of 12 GB of graphics memory in use')
    expect(cardLine(null, null)).toBeNull()
  })
})
