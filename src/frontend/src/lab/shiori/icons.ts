import { CircleHelp, Gavel, Lightbulb, ListChecks, type LucideIcon } from 'lucide-react'
import type { BoardKind } from './board'
import type { Mode } from './modes'

/** A mode and the kind of item it produces share an icon: brainstorm gives ideas, decide decisions. */
export const MODE_ICONS: Record<Mode, LucideIcon> = {
  brainstorm: Lightbulb,
  decide: Gavel,
  organise: ListChecks,
}

export const KIND_ICONS: Record<BoardKind, LucideIcon> = {
  idea: Lightbulb,
  doubt: CircleHelp,
  decision: Gavel,
  task: ListChecks,
}
