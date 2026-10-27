import type { GpuState, GpuView } from '../api/types'

export type GpuTone = 'calm' | 'waiting' | 'trouble'

export interface GpuBadge {
  state: GpuState
  text: string
  tone: GpuTone
  /** The longer story, for the tooltip. */
  title: string
}

/** What the top bar says about the graphics card; nothing while it has no story to tell. */
export function gpuBadge(view: GpuView | undefined): GpuBadge | undefined {
  if (!view || view.state === 'Off' || view.state === 'Free') return undefined
  const model = view.modelName ?? view.model ?? 'The local model'
  const holders = view.holders.length > 0 ? view.holders.join(', ') : 'another program'
  const badge = (text: string, tone: GpuTone): GpuBadge => ({ state: view.state, text, tone, title: view.detail ?? text })
  switch (view.state) {
    case 'Loading':
      return badge(`Loading ${model}…`, 'calm')
    case 'Serving':
      return badge(`${model} on the graphics card`, 'calm')
    case 'WaitingForMemory':
      return badge(`${model} waits for graphics memory`, 'waiting')
    case 'Yielding':
      return badge(`Freeing the graphics card for ${holders}`, 'waiting')
    case 'Lent':
      return badge(`Graphics card lent to ${holders}`, 'waiting')
    case 'Failed':
      return badge(view.detail?.includes('graphics memory') ? `${model}: not enough graphics memory` : `${model} did not load`, 'trouble')
  }
}
