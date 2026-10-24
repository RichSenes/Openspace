import type { AgentEngine, DeskView, EnginesView, LimitPause, ModelOption } from '../api/types'

/** What runs an agent, as the desk forms edit it. */
export interface EngineChoice {
  engine: AgentEngine
  agentName: string
  model: string
}

export const ENGINE_LABEL: Record<AgentEngine, string> = { OpenCode: 'OpenCode', ClaudeCode: 'Claude Code' }

/** The models to offer: the engine's, plus the current one when the engine does not list it. */
export function modelOptions(engines: EnginesView | undefined, engine: AgentEngine, current: string): ModelOption[] {
  const listed = (engine === 'ClaudeCode' ? engines?.claudeCodeModels : engines?.openCodeModels) ?? []
  return current && !listed.some((m) => m.id === current) ? [{ id: current, name: `${current} (not listed now)` }, ...listed] : listed
}

/** The account a desk spends: Claude's, or the OpenCode provider of its model. As the API names it. */
export function scopeOf(desk: Pick<DeskView, 'engine' | 'model'>): string | null {
  if (desk.engine === 'ClaudeCode') return 'claude-code'
  const slash = desk.model?.indexOf('/') ?? -1
  return slash > 0 ? `opencode:${desk.model!.slice(0, slash)}` : null
}

/** The pause keeping a desk's tasks in the queue, if its account is over its limits. */
export function pauseOf(engines: EnginesView | undefined, desk: Pick<DeskView, 'engine' | 'model'> | undefined): LimitPause | undefined {
  const scope = desk ? scopeOf(desk) : null
  return scope ? engines?.pauses?.find((p) => p.scope === scope) : undefined
}
