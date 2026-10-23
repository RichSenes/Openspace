import type { TaskStatus, WorkTaskView } from '../api/types'

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

export type ReviewVerdict = 'ok' | 'changes'

/**
 * What the reviewer said about the work as it is now: its latest note, as long as the work
 * has not been sent back since. Undefined when nobody reviewed it.
 */
export function reviewVerdictOf(task: WorkTaskView): ReviewVerdict | undefined {
  if (task.status !== 'Review') return undefined
  const latest = [...task.notes].reverse().find((n) => n.authorKind === 'Reviewer')
  if (!latest) return undefined
  return latest.text.startsWith('OK') ? 'ok' : 'changes'
}
