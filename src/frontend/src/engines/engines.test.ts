import { describe, expect, it } from 'vitest'
import type { EnginesView } from '../api/types'
import { modelOptions, pauseOf, scopeOf } from './engines'

const engines: EnginesView = {
  openCode: true,
  openCodeModels: [{ id: 'opencode/big-pickle', name: 'Big Pickle · OpenCode' }],
  claudeCode: true,
  claudeCodeModels: [{ id: 'sonnet', name: 'Sonnet, balanced' }],
  claudeUsage: null,
  supervisor: 'ClaudeCode',
  reviewer: 'ClaudeCode',
}

describe('modelOptions', () => {
  it('offers the models of the chosen engine', () => {
    expect(modelOptions(engines, 'OpenCode', '').map((m) => m.id)).toEqual(['opencode/big-pickle'])
    expect(modelOptions(engines, 'ClaudeCode', '').map((m) => m.id)).toEqual(['sonnet'])
  })

  it('keeps a model the engine no longer lists, so it is never lost by accident', () => {
    expect(modelOptions(engines, 'OpenCode', 'local/tiny-model')[0]).toEqual({ id: 'local/tiny-model', name: 'local/tiny-model (not listed now)' })
  })

  it('offers nothing while the engines are unknown', () => {
    expect(modelOptions(undefined, 'OpenCode', '')).toEqual([])
  })
})

describe('scopeOf and pauseOf', () => {
  it('names the account a desk spends, as the API does', () => {
    expect(scopeOf({ engine: 'ClaudeCode', model: 'sonnet' })).toBe('claude-code')
    expect(scopeOf({ engine: 'OpenCode', model: 'opencode/big-pickle' })).toBe('opencode:opencode')
    expect(scopeOf({ engine: 'OpenCode', model: null })).toBeNull()
  })

  it('finds the pause of a desk on a paused account only', () => {
    const engines = { pauses: [{ scope: 'opencode:opencode', label: 'OpenCode models of opencode', until: '2026-10-25T00:00:00Z' }] } as EnginesView
    expect(pauseOf(engines, { engine: 'OpenCode', model: 'opencode/deepseek-v4.1-flash-free' })?.until).toBe('2026-10-25T00:00:00Z')
    expect(pauseOf(engines, { engine: 'ClaudeCode', model: 'sonnet' })).toBeUndefined()
  })
})
