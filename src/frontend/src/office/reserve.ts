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

/** How a reserve on Claude Code is written, before its model: claude-code/sonnet. */
export const CLAUDE_RESERVE = 'claude-code/'

/**
 * The models nobody lists: a typo, or a provider not connected yet. OpenCode's are checked
 * against OpenCode's list, Claude Code's (claude-code/sonnet) against Claude Code's.
 */
export function unknownModels(models: string[], openCode: ModelOption[] | undefined, claude?: ModelOption[]): string[] {
  const known = (list: ModelOption[] | undefined) => (list && list.length > 0 ? new Set(list.map((m) => m.id)) : undefined)
  const [open, code] = [known(openCode), known(claude)]
  return models.filter((m) =>
    m.startsWith(CLAUDE_RESERVE) ? code !== undefined && !code.has(m.slice(CLAUDE_RESERVE.length)) : open !== undefined && !open.has(m),
  )
}
