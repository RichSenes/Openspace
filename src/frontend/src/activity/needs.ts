import type { NeedsYouItem, NeedsYouKind } from '../api/types'

/** Where an item opens: the Board on a task, or the Routines of an office. */
export interface NeedsYouTarget {
  officeId: string
  view: 'board' | 'routines'
  taskId?: string
}

/** The kinds in the order they are shown, each with its heading and what opening it does. */
export const GROUPS: { kind: NeedsYouKind; heading: string; open: string }[] = [
  { kind: 'TaskToApprove', heading: 'Tasks to approve', open: 'Open on the Board' },
  { kind: 'TaskToReview', heading: 'To review', open: 'Review it' },
  { kind: 'TaskFailed', heading: 'Failed tasks', open: 'Open on the Board' },
  { kind: 'Proposal', heading: 'Routines ask', open: 'Open in Routines' },
  { kind: 'RoutineFailed', heading: 'Routines that failed', open: 'See the run' },
]

/** The items of each kind that has any, in the order of GROUPS. */
export function grouped(items: NeedsYouItem[]): { kind: NeedsYouKind; heading: string; open: string; items: NeedsYouItem[] }[] {
  return GROUPS.map((g) => ({ ...g, items: items.filter((i) => i.kind === g.kind) })).filter((g) => g.items.length > 0)
}

/** Where opening an item takes the person. */
export function targetOf(item: NeedsYouItem): NeedsYouTarget {
  return item.kind === 'Proposal' || item.kind === 'RoutineFailed'
    ? { officeId: item.officeId, view: 'routines' }
    : { officeId: item.officeId, view: 'board', taskId: item.id }
}

/** "5 min", "3 h", "2 days": how long it has waited. */
export function waited(since: string, now: number): string {
  const minutes = Math.max(0, Math.floor((now - new Date(since).getTime()) / 60_000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} min`
  const hours = Math.floor(minutes / 60)
  if (hours < 48) return `${hours} h`
  return `${Math.floor(hours / 24)} days`
}
