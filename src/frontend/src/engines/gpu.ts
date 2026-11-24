import type { GpuState, GpuView } from '../api/types'

export type GpuTone = 'calm' | 'waiting' | 'trouble'

export interface GpuBadge {
  state: GpuState
  text: string
  tone: GpuTone
  /** The longer story, for the tooltip. */
  title: string
  /** Openspace's own programs hold memory a waiting model needs: a button can free it. */
  canMakeRoom: boolean
}

/** "Shiori's voice 5.9 GB, ComfyUI 8.3 GB": who has the card's memory. */
export function gpuUsing(view: GpuView): string | undefined {
  const using = view.using ?? []
  if (!using.length) return undefined
  return using.map((u) => `${u.name} ${(u.miB / 1024).toFixed(1)} GB`).join(', ')
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
    title: [
      view.detail ?? text,
      ...(users ? [`Used by: ${users.full.join('; ')}`] : []),
      ...(gpuUsing(view) ? [`On the card: ${gpuUsing(view)}`] : []),
    ].join('\n'),
    canMakeRoom: view.state === 'WaitingForMemory' && (view.using ?? []).some((u) => u.openspace && u.name !== "Shiori's ears"),
  })
  switch (view.state) {
    case 'Loading':
      return badge(users ? `Loading ${model} for ${users.short}…` : `Loading ${model}…`, 'calm')
    case 'Serving':
      return badge(users ? `${model} · ${users.short}` : `${model} · idle`, 'calm')
    case 'WaitingForMemory':
      // Named after the biggest holder, so it is clear what to close or free.
      return badge(view.using?.length ? `${model} waits for graphics memory held by ${view.using[0].name}` : `${model} waits for graphics memory`, 'waiting')
    case 'Yielding':
      return badge(`Freeing the graphics card for ${holders}`, 'waiting')
    case 'Lent':
      return badge(`Graphics card lent to ${holders}`, 'waiting')
    case 'Failed':
      return badge(view.detail?.includes('graphics memory') ? `${model}: not enough graphics memory` : `${model} did not load`, 'trouble')
  }
}
