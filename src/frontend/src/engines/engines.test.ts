import { describe, expect, it } from 'vitest'
import type { EnginesView } from '../api/types'
import { modelOptions } from './engines'

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
