import { describe, expect, it } from 'vitest'
import type { AuditEntryView, AuditKind } from '../api/types'
import { filterLog } from './auditLabels'

const entry = (kind: AuditKind): AuditEntryView => ({
  id: 1,
  at: '2015-06-24T09:00:00Z',
  kind,
  summary: kind,
  detail: null,
  succeeded: null,
})

describe('filterLog', () => {
  const all = (['Lifecycle', 'Step', 'Read', 'Change', 'Command', 'Search', 'Permission', 'Note'] as AuditKind[]).map(entry)

  it('shows everything on "all"', () => {
    expect(filterLog(all, 'all')).toHaveLength(all.length)
  })

  it('keeps only what changed something or needs attention on "changes"', () => {
    expect(filterLog(all, 'changes').map((e) => e.kind)).toEqual(['Lifecycle', 'Change', 'Command', 'Permission', 'Note'])
  })
})
