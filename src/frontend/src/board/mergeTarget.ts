import type { BranchList } from '../api/types'

/** The branch a merge should target by default: the one checked out, else main or master, else the first. */
export function defaultMergeTarget(list: BranchList | undefined): string {
  if (!list || list.branches.length === 0) return ''
  if (list.current && list.branches.includes(list.current)) return list.current
  return list.branches.find((b) => b === 'main' || b === 'master') ?? list.branches[0]
}
