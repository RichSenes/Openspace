import type { ModelOption } from '../api/types'

/** The most reserve models an office keeps, as the backend allows. */
export const MAX_RESERVE_MODELS = 4

/** One provider/model per line, kept once, in order. */
export function parseReserve(text: string): string[] {
  const models: string[] = []
  for (const line of text.split('\n')) {
    const model = line.trim()
    if (model && !models.includes(model)) models.push(model)
  }
  return models
}

/** The models OpenCode does not list: a typo, or a provider not connected yet. */
export function unknownModels(models: string[], known: ModelOption[] | undefined): string[] {
  if (!known || known.length === 0) return []
  const ids = new Set(known.map((m) => m.id))
  return models.filter((m) => !ids.has(m))
}
