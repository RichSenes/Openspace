import type { LocalAiSettings, LocalModelSetting } from '../api/types'

/** A model as its form edits it: the arguments as text, one per line. */
export interface ModelDraft extends Omit<LocalModelSetting, 'arguments'> {
  arguments: string
  /** Kept while the person edits the id, so React keeps the same card. */
  key: string
}

export interface LocalAiDraft extends Omit<LocalAiSettings, 'models' | 'decider'> {
  models: ModelDraft[]
  decider: { command: string; arguments: string; url: string }
}

let counter = 0
const nextKey = () => `m${++counter}`

/** One argument per line; blank lines dropped. */
export const linesOf = (text: string) =>
  text
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean)

export function draftOf(settings: LocalAiSettings): LocalAiDraft {
  return {
    ...settings,
    models: settings.models.map((m) => ({ ...m, arguments: m.arguments.join('\n'), key: nextKey() })),
    decider: { ...settings.decider, arguments: settings.decider.arguments.join('\n') },
  }
}

export function settingsOf(draft: LocalAiDraft): LocalAiSettings {
  return {
    ...draft,
    models: draft.models.map(({ key: _key, ...m }) => ({ ...m, arguments: linesOf(m.arguments) })),
    decider: { ...draft.decider, arguments: linesOf(draft.decider.arguments) },
  }
}

export function modelOf(draft: ModelDraft): LocalModelSetting {
  const { key: _key, ...m } = draft
  return { ...m, arguments: linesOf(m.arguments) }
}

/** A new model to fill in, with a name not taken yet. */
export function newModel(taken: string[]): ModelDraft {
  let id = 'model'
  for (let i = 2; taken.includes(id); i++) id = `model-${i}`
  return { key: nextKey(), id, name: '', command: '', arguments: '', memoryMiB: 0, context: 32768 }
}

/** Whether the draft differs from what is saved, compared as it would be sent. */
export function changed(saved: LocalAiSettings, draft: LocalAiDraft): boolean {
  return JSON.stringify(settingsOf(draft)) !== JSON.stringify(saved)
}

/** "6.2 of 12 GB in use", or nothing when the card cannot be read. */
export function cardLine(cardMiB: number | null, usedMiB: number | null): string | null {
  if (cardMiB === null) return null
  const gb = (mib: number) => (mib / 1024).toFixed(1).replace(/\.0$/, '')
  return usedMiB === null ? `${gb(cardMiB)} GB of graphics memory` : `${gb(usedMiB)} of ${gb(cardMiB)} GB of graphics memory in use`
}
