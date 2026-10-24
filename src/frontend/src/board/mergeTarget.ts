import type { BranchList } from '../api/types'

/** The branch a merge should target by default: the one checked out, else main or master, else the first. */
export function defaultMergeTarget(list: BranchList | undefined): string {
  if (!list || list.branches.length === 0) return ''
  if (list.current && list.branches.includes(list.current)) return list.current
  return list.branches.find((b) => b === 'main' || b === 'master') ?? list.branches[0]
}

/** Why nothing can be merged into the target right now, said before trying; null when it can. */
export function mergeBlocker(list: BranchList | undefined, target: string): string | null {
  const changes = list?.uncommittedChanges ?? 0
  if (!list || target !== list.current || changes === 0) return null
  return `${target} is checked out with ${changes} uncommitted ${changes === 1 ? 'change' : 'changes'}: nothing can be merged into it until you commit or stash them. Or choose another branch.`
}
