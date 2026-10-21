import type { AuditEntryView, AuditKind } from '../api/types'

export const KIND_LABEL: Record<AuditKind, string> = {
  Lifecycle: 'Task',
  Step: 'Step',
  Read: 'Read',
  Change: 'Change',
  Command: 'Command',
  Search: 'Search',
  Permission: 'Permission',
  Note: 'Note',
  Procedure: 'Procedure',
  Other: 'Tool',
}

export type LogFilter = 'all' | 'changes'

/** "Changes" keeps what altered something or needs a person's eye; reads and searches are noise there. */
export function filterLog(entries: AuditEntryView[], filter: LogFilter): AuditEntryView[] {
  if (filter === 'all') return entries
  return entries.filter((e) => e.kind !== 'Read' && e.kind !== 'Search' && e.kind !== 'Step')
}
