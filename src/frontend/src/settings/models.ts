import type { EngineChoice, FunctionChoice, ModelEngine, ModelOption, ModelsSettings, ModelsView } from '../api/types'

/** The lists a model is chosen from; undefined while an engine has not answered. */
export interface ModelLists {
  claudeCode?: ModelOption[]
  openCode?: ModelOption[]
  local?: ModelOption[]
}

/** What a blank model means on each engine. */
export const DEFAULT_MODEL: Record<ModelEngine, string> = {
  ClaudeCode: 'Claude Code’s default',
  OpenCode: 'OpenCode’s default',
  Local: 'The first model of Local AI',
}

/** What is sent: the view without what is only shown. */
export function settingsOf(view: ModelsView): ModelsSettings {
  return { shiori: view.shiori, reviewer: view.reviewer, digests: view.digests, journals: view.journals }
}

export function changed(saved: ModelsView, draft: ModelsSettings): boolean {
  return JSON.stringify(settingsOf(saved)) !== JSON.stringify(draft)
}

export function listOf(engine: ModelEngine, lists: ModelLists): ModelOption[] | undefined {
  return engine === 'ClaudeCode' ? lists.claudeCode : engine === 'OpenCode' ? lists.openCode : lists.local
}

/**
 * A model nobody lists: a typo, a provider not connected yet, a local model removed. Only
 * said when the list is known and has something in it, as for desks' reserve models.
 */
export function isUnknown(model: string | null, list: ModelOption[] | undefined): boolean {
  return model !== null && list !== undefined && list.length > 0 && !list.some((m) => m.id === model)
}

/** Another engine for a digest or a journal: the model goes back to that engine's default. */
export function withEngine(choice: FunctionChoice, engine: ModelEngine): FunctionChoice {
  return engine === choice.engine ? choice : { engine, model: null }
}

/** Why the form cannot be saved as it is, or null; the server checks the same. */
export function problemOf(draft: ModelsSettings): string | null {
  if (draft.reviewer.engine === 'OpenCode' && !draft.reviewer.openCodeModel) return 'The reviewer on OpenCode needs a model: choose one, or No review.'
  return null
}

/**
 * Who does the work now, in words. Claude Code chosen but not on this machine means OpenCode
 * stands in, as the planner and the reviewer have always done; the reviewer stands in only
 * with a model, otherwise nobody reviews.
 */
export function runsOn(choice: EngineChoice, claudeHere: boolean, kind: 'shiori' | 'reviewer'): string {
  if (choice.engine === null) return 'Nobody: you review finished work yourself.'
  if (choice.engine === 'OpenCode') return 'OpenCode.'
  if (claudeHere) return 'Claude Code.'
  if (kind === 'reviewer' && !choice.openCodeModel) return 'Nobody, until Claude Code is on this machine.'
  return 'OpenCode, until Claude Code is on this machine.'
}
