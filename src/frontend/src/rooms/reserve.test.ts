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
})
