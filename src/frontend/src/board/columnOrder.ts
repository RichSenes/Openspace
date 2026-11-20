import type { TaskStatus } from '../api/types'
import { COLUMNS, type Column } from './taskRules'

/** The columns that wait for the person, in the order they are looked at: check, fix, approve. */
const NEEDS_YOU: TaskStatus[] = ['Review', 'Failed', 'Proposed']

/**
 * The board's columns in the order to show them. Wide screens keep the order of the work
 * (proposed to done); narrow ones, where only two or three columns are in sight, put first
 * the columns that need the person, and then the others in the order of the work.
 */
export function boardColumns(narrow: boolean): Column[] {
  if (!narrow) return COLUMNS
  const first = NEEDS_YOU.map((s) => COLUMNS.find((c) => c.status === s)).filter((c): c is Column => c !== undefined)
  return [...first, ...COLUMNS.filter((c) => !NEEDS_YOU.includes(c.status))]
}
