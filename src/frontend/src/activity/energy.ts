import type { Energy } from '../art/agentStates'
import type { AgentState } from '../art/agentStates'
import type { DeskContext } from '../api/types'

/** Replaces the desk's previous energy with the new one. */
export function mergeContext(list: DeskContext[] | undefined, update: DeskContext): DeskContext[] {
  return [...(list ?? []).filter((c) => c.deskId !== update.deskId), update]
}

/** The latest energy per desk of one office. */
export function contextByDesk(list: DeskContext[] | undefined, officeId: string): Map<string, DeskContext> {
  return new Map((list ?? []).filter((c) => c.officeId === officeId).map((c) => [c.deskId, c]))
}

/** States without a conversation on the desk: no battery there. */
const RESTING: ReadonlySet<AgentState> = new Set(['idle', 'queued'])

/**
 * What the battery of a desk shows at a moment: nothing while the desk rests or the
 * model's window is unknown, else the energy left and how long ago it came back.
 */
export function energyOf(context: DeskContext | undefined, state: AgentState, now: number): Energy | undefined {
  if (!context || context.left === null || RESTING.has(state)) return undefined
  const refreshed = context.refreshedAt ? Date.parse(context.refreshedAt) : NaN
  return Number.isNaN(refreshed) ? { left: context.left } : { left: context.left, sinceRefresh: (now - refreshed) / 1000 }
}

/** The energy in words, for the agent's card: how much room is left and how often it was summarised. */
export function energyWords(context: DeskContext | undefined): string | null {
  if (!context || context.left === null) return null
  const left = Math.round(context.left * 100)
  const summaries = context.compactions === 0 ? '' : context.compactions === 1 ? ' · summarised once' : ` · summarised ${context.compactions} times`
  return `${left}% of its conversation left${summaries}`
}
