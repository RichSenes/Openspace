import { describe, expect, it } from 'vitest'
import type { ModelsView } from '../api/types'
import { changed, isUnknown, listOf, problemOf, runsOn, settingsOf, withEngine } from './models'

const saved: ModelsView = {
  shiori: { engine: 'ClaudeCode', claudeModel: 'opus', openCodeModel: 'github-copilot/gpt-5' },
  reviewer: { engine: 'ClaudeCode', claudeModel: null, openCodeModel: null },
  digests: { engine: 'ClaudeCode', model: 'sonnet' },
  journals: { engine: 'Local', model: null },
  localModels: [{ id: 'local/gemma', name: 'Gemma' }],
  decider: { configured: false, command: '', url: 'http://127.0.0.1:8017/' },
}

describe('models per function', () => {
  it('sends the choices without what is only shown, and sees a change', () => {
    const draft = settingsOf(saved)
    expect(draft).not.toHaveProperty('localModels')
    expect(changed(saved, draft)).toBe(false)
    expect(changed(saved, { ...draft, journals: { engine: 'Local', model: 'local/gemma' } })).toBe(true)
  })

  it('flags a model nobody lists, only once the list is known and not empty', () => {
    const lists = { claudeCode: [{ id: 'opus', name: 'Opus' }], openCode: [], local: saved.localModels }
    expect(isUnknown('local/qwen', listOf('Local', lists))).toBe(true)
    expect(isUnknown('local/gemma', listOf('Local', lists))).toBe(false)
    expect(isUnknown('github-copilot/gpt-5', listOf('OpenCode', lists))).toBe(false)
    expect(isUnknown('opus', undefined)).toBe(false)
    expect(isUnknown(null, lists.claudeCode)).toBe(false)
  })

  it('goes back to the default model when the engine of a function changes', () => {
    expect(withEngine(saved.digests, 'Local')).toEqual({ engine: 'Local', model: null })
    expect(withEngine(saved.digests, 'ClaudeCode')).toBe(saved.digests)
  })

  it('refuses an OpenCode reviewer without a model', () => {
    expect(problemOf({ ...settingsOf(saved), reviewer: { engine: 'OpenCode', claudeModel: null, openCodeModel: null } })).toMatch(/needs a model/)
    expect(problemOf(settingsOf(saved))).toBeNull()
  })

  it('tells who stands in while Claude Code is missing', () => {
    expect(runsOn(saved.shiori, true, 'shiori')).toBe('Claude Code.')
    expect(runsOn(saved.shiori, false, 'shiori')).toBe('OpenCode, until Claude Code is on this machine.')
    expect(runsOn(saved.reviewer, false, 'reviewer')).toBe('Nobody, until Claude Code is on this machine.')
    expect(runsOn({ ...saved.reviewer, engine: null }, true, 'reviewer')).toMatch(/yourself/)
  })
})
