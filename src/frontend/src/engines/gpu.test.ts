import { describe, expect, it } from 'vitest'
import type { GpuView } from '../api/types'
import { gpuBadge } from './gpu'

const view = (over: Partial<GpuView>): GpuView => ({ state: 'Free', model: null, modelName: null, holders: [], detail: null, routineTurn: false, routinesWaiting: [], ...over })

describe('gpuBadge', () => {
  it('stays quiet while local models are off or the card is free', () => {
    expect(gpuBadge(undefined)).toBeUndefined()
    expect(gpuBadge(view({ state: 'Off' }))).toBeUndefined()
    expect(gpuBadge(view({ state: 'Free', detail: 'Unloaded Gemma: unused for 10 minutes.' }))).toBeUndefined()
  })

  it('names the model working on the card', () => {
    expect(gpuBadge(view({ state: 'Serving', model: 'local/gemma', modelName: 'Gemma' }))).toMatchObject({ text: 'Gemma · idle', tone: 'calm' })
    expect(gpuBadge(view({ state: 'Loading', model: 'local/tiny' }))?.text).toBe('Loading local/tiny…')
  })

  it('names the programs the card is lent to, with the detail as the tooltip', () => {
    const badge = gpuBadge(view({ state: 'Lent', holders: ['Image studio', 'Trainer'], detail: 'Local desks wait until the graphics card is free again.' }))
    expect(badge).toMatchObject({ text: 'Graphics card lent to Image studio, Trainer', tone: 'waiting' })
    expect(badge?.title).toBe('Local desks wait until the graphics card is free again.')
  })

  it('says who holds the memory a model waits for, and offers to free what is Openspace\'s', () => {
    const waiting = view({
      state: 'WaitingForMemory',
      modelName: 'Gemma',
      detail: '3900 MiB free on the graphics card, 10300 needed.',
      using: [
        { name: "Shiori's voice", miB: 6050, openspace: true },
        { name: "Shiori's ears", miB: 1082, openspace: true },
      ],
    })
    const badge = gpuBadge(waiting)
    expect(badge?.text).toBe("Gemma waits for graphics memory held by Shiori's voice")
    expect(badge?.title).toContain("On the card: Shiori's voice 5.9 GB, Shiori's ears 1.1 GB")
    expect(badge?.canMakeRoom).toBe(true)
    expect(gpuBadge({ ...waiting, using: [{ name: 'ComfyUI', miB: 9000, openspace: false }] })?.canMakeRoom).toBe(false)
  })

  it('says in the bar when a model did not load for lack of graphics memory', () => {
    expect(gpuBadge(view({ state: 'Failed', modelName: 'Gemma', detail: 'The model server stopped while loading (not enough graphics memory): trying again at 17:04.' }))?.text).toBe(
      'Gemma: not enough graphics memory',
    )
  })

  it('says when a model did not load', () => {
    expect(gpuBadge(view({ state: 'Failed', modelName: 'Gemma', detail: 'The model did not answer: trying again at 10:05.' }))).toMatchObject({ text: 'Gemma did not load', tone: 'trouble' })
  })

  it('says who uses the model: the office and desk of a task, or a routine, the rest in the tooltip', () => {
    const one = gpuBadge(view({ state: 'Serving', modelName: 'Gemma', users: ['Karuta · Android: Saved words page'] }))
    expect(one?.text).toBe('Gemma · Karuta · Android')
    expect(one?.title).toContain('Used by: Karuta · Android: Saved words page')
    const routine = gpuBadge(view({ state: 'Serving', modelName: 'Gemma', routineTurn: true, routinesWaiting: ['CV'] }))
    expect(routine?.text).toBe('Gemma · routine CV')
    const both = gpuBadge(view({ state: 'Loading', modelName: 'Gemma', users: ['Karuta · UI: Keyboard'], routineTurn: true, routinesWaiting: ['CV'] }))
    expect(both?.text).toBe('Loading Gemma for Karuta · UI +1…')
  })
})
