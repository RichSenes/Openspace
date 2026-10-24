import type { RoundUsageView, TaskNoteView, TaskStatus, WorkTaskView } from '../api/types'

export interface Column {
  status: TaskStatus
  label: string
  /** Shown when the column is empty: says what would make a task appear here. */
  emptyHint: string
}

export const COLUMNS: Column[] = [
  { status: 'Proposed', label: 'Proposed', emptyHint: 'Tasks without a desk wait here for your approval.' },
  { status: 'Queued', label: 'Queued', emptyHint: 'Approved tasks wait here for a free seat.' },
  { status: 'Running', label: 'Running', emptyHint: 'Nobody is working right now.' },
  { status: 'Review', label: 'Review', emptyHint: 'Finished work waits here for you to check it.' },
  { status: 'Failed', label: 'Failed', emptyHint: 'Tasks an agent could not finish show up here.' },
  { status: 'Done', label: 'Done', emptyHint: 'Accepted work ends up here.' },
]

/** Groups tasks by board column, keeping the API order (oldest first). Discarded tasks are left out. */
export function groupByColumn(tasks: WorkTaskView[]): Map<TaskStatus, WorkTaskView[]> {
  const groups = new Map<TaskStatus, WorkTaskView[]>(COLUMNS.map((c) => [c.status, []]))
  for (const task of tasks) {
    groups.get(task.status)?.push(task)
  }
  return groups
}

export type TaskAction = 'approve' | 'accept' | 'send-back' | 'retry' | 'discard'

/** What a person can do with a task in a given status, mirroring the domain rules. */
export function actionsFor(status: TaskStatus): TaskAction[] {
  switch (status) {
    case 'Proposed':
      return ['approve', 'discard']
    case 'Queued':
    case 'Running':
      return ['discard']
    case 'Review':
      return ['accept', 'send-back', 'discard']
    case 'Failed':
      return ['retry', 'discard']
    case 'Done':
    case 'Discarded':
      return []
  }
}

export type EpicFilter = 'all' | 'none' | string

/** Keeps the tasks of one epic, the tasks outside any epic, or everything. */
export function filterByEpic(tasks: WorkTaskView[], filter: EpicFilter): WorkTaskView[] {
  if (filter === 'all') return tasks
  if (filter === 'none') return tasks.filter((t) => t.epicId === null)
  return tasks.filter((t) => t.epicId === filter)
}

/** Time of an ISO date from the API, whatever its fraction of a second. */
export const timeOf = (iso: string) => Date.parse(iso.replace(/(\.\d{3})\d+/, '$1'))

/** Longest a review may take before Openspace stops saying one is under way. */
export const REVIEW_PATIENCE_MS = 30 * 60_000

export type ReviewVerdict = 'ok' | 'changes' | 'reviewing'

/**
 * What the reviewer said about the work as it is now: its note written since the work last
 * reached review. Older notes are about work since sent back. While none has come yet and
 * a reviewer is on, the review is under way, unless it failed or takes far too long.
 */
export function reviewVerdictOf(task: WorkTaskView, reviewerOn = false, now = Date.now()): ReviewVerdict | undefined {
  if (task.status !== 'Review') return undefined
  const since = timeOf(task.statusChangedAt)
  const latest = currentReview(task)
  if (latest) return latest.text.startsWith('OK') ? 'ok' : 'changes'
  const failed = task.notes.some((n) => n.authorKind === 'System' && timeOf(n.writtenAt) >= since && n.text.startsWith('The review by Claude could not run'))
  return reviewerOn && !failed && now - since < REVIEW_PATIENCE_MS ? 'reviewing' : undefined
}

/** The reviewer's note about the work as it is now, if it came. */
export function currentReview(task: WorkTaskView): TaskNoteView | undefined {
  const since = timeOf(task.statusChangedAt)
  return [...task.notes].reverse().find((n) => n.authorKind === 'Reviewer' && timeOf(n.writtenAt) >= since)
}

/** How many checks a person tried by hand, out of those listed. */
export function checksDone(task: WorkTaskView): { done: number; total: number } {
  return { done: task.manualChecks.filter((c) => c.checkedAt).length, total: task.manualChecks.length }
}

/** Accepted work on its own branch, not merged anywhere yet, in the order it was accepted. */
export function awaitingMerge(tasks: readonly WorkTaskView[]): WorkTaskView[] {
  return tasks
    .filter((t) => t.status === 'Done' && t.branch !== null && t.mergedInto === null)
    .toSorted((a, b) => timeOf(a.statusChangedAt) - timeOf(b.statusChangedAt))
}

/** Token counts in a few characters: 950, 12.4k, 1.3M. */
export function compactTokens(n: number): string {
  if (n < 1000) return String(n)
  if (n < 1_000_000) return `${(n / 1000).toFixed(n < 10_000 ? 1 : 0).replace(/\.0$/, '')}k`
  return `${(n / 1_000_000).toFixed(1).replace(/\.0$/, '')}M`
}

/** Everything a task's rounds spent. */
export function totalTokens(usage: RoundUsageView[]): RoundUsageView {
  return usage.reduce(
    (sum, r) => ({ ...sum, input: sum.input + r.input, output: sum.output + r.output, cacheRead: sum.cacheRead + r.cacheRead, cacheWrite: sum.cacheWrite + r.cacheWrite }),
    { round: 0, input: 0, output: 0, cacheRead: 0, cacheWrite: 0 },
  )
}
