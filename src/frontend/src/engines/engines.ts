import type { AgentEngine, EnginesView, ModelOption } from '../api/types'

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
