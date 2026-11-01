import type { GpuState, GpuView } from '../api/types'

export type GpuTone = 'calm' | 'waiting' | 'trouble'

export interface GpuBadge {
  state: GpuState
  text: string
  tone: GpuTone
  /** The longer story, for the tooltip. */
  title: string
}

/**
 * Who works on the loaded model: tasks as "office · desk", routines by name. The bar names
 * the first and counts the others; the tooltip lists them all, task titles included.
 */
export function gpuUsers(view: GpuView): { short: string; full: string[] } | undefined {
  const tasks = view.users ?? []
  const routines = (view.routineTurn ? (view.routinesWaiting ?? []) : []).map((r) => `routine ${r}`)
  const all = [...tasks, ...routines]
  if (all.length === 0) return undefined
  const first = all[0].split(':')[0]
  return { short: all.length === 1 ? first : `${first} +${all.length - 1}`, full: all }
}

/** What the top bar says about the graphics card; nothing while it has no story to tell. */
export function gpuBadge(view: GpuView | undefined): GpuBadge | undefined {
  if (!view || view.state === 'Off' || view.state === 'Free') return undefined
  const model = view.modelName ?? view.model ?? 'The local model'
  const holders = view.holders.length > 0 ? view.holders.join(', ') : 'another program'
  const users = gpuUsers(view)
  const badge = (text: string, tone: GpuTone): GpuBadge => ({
    state: view.state,
    text,
    tone,
    title: [view.detail ?? text, ...(users ? [`Used by: ${users.full.join('; ')}`] : [])].join('\n'),
  })
  switch (view.state) {
    case 'Loading':
      return badge(users ? `Loading ${model} for ${users.short}…` : `Loading ${model}…`, 'calm')
    case 'Serving':
      return badge(users ? `${model} · ${users.short}` : `${model} · idle`, 'calm')
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
