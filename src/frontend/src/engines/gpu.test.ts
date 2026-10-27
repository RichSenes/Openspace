import { describe, expect, it } from 'vitest'
import type { GpuView } from '../api/types'
import { gpuBadge } from './gpu'

const view = (over: Partial<GpuView>): GpuView => ({ state: 'Free', model: null, modelName: null, holders: [], detail: null, ...over })

describe('gpuBadge', () => {
  it('stays quiet while local models are off or the card is free', () => {
    expect(gpuBadge(undefined)).toBeUndefined()
    expect(gpuBadge(view({ state: 'Off' }))).toBeUndefined()
    expect(gpuBadge(view({ state: 'Free', detail: 'Unloaded Gemma: unused for 10 minutes.' }))).toBeUndefined()
  })

  it('names the model working on the card', () => {
    expect(gpuBadge(view({ state: 'Serving', model: 'local/gemma', modelName: 'Gemma' }))).toMatchObject({ text: 'Gemma on the graphics card', tone: 'calm' })
    expect(gpuBadge(view({ state: 'Loading', model: 'local/tiny' }))?.text).toBe('Loading local/tiny…')
  })

  it('names the programs the card is lent to, with the detail as the tooltip', () => {
    const badge = gpuBadge(view({ state: 'Lent', holders: ['Image studio', 'Trainer'], detail: 'Local desks wait until the graphics card is free again.' }))
    expect(badge).toMatchObject({ text: 'Graphics card lent to Image studio, Trainer', tone: 'waiting' })
    expect(badge?.title).toBe('Local desks wait until the graphics card is free again.')
  })

  it('says when a model did not load', () => {
    expect(gpuBadge(view({ state: 'Failed', modelName: 'Gemma', detail: 'The model did not answer: trying again at 10:05.' }))).toMatchObject({ text: 'Gemma did not load', tone: 'trouble' })
  })
})
