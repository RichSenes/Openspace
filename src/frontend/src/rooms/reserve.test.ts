import { describe, expect, it } from 'vitest'
import { parseReserve, unknownModels } from './reserve'

describe('reserve models', () => {
  it('keeps one model per line, once and in order', () => {
    expect(parseReserve(' nvidia/qwen3-coder \n\nmistral/devstral\nnvidia/qwen3-coder')).toEqual(['nvidia/qwen3-coder', 'mistral/devstral'])
  })

  it('points out the models OpenCode does not list', () => {
    const known = [{ id: 'nvidia/qwen3-coder', name: 'Qwen3 Coder' }]
    expect(unknownModels(['nvidia/qwen3-coder', 'mistral/devstral'], known)).toEqual(['mistral/devstral'])
  })

  it('says nothing while OpenCode is not reachable', () => {
    expect(unknownModels(['mistral/devstral'], [])).toEqual([])
    expect(unknownModels(['mistral/devstral'], undefined)).toEqual([])
  })

  it('checks claude-code reserves against the Claude Code models', () => {
    const open = [{ id: 'nvidia/qwen3-coder', name: 'Qwen3 Coder' }]
    const claude = [{ id: 'sonnet', name: 'Sonnet' }]
    expect(unknownModels(['claude-code/sonnet', 'claude-code/sonet', 'nvidia/qwen3-coder'], open, claude)).toEqual(['claude-code/sonet'])
    expect(unknownModels(['claude-code/sonnet'], open, undefined)).toEqual([])
  })
})
