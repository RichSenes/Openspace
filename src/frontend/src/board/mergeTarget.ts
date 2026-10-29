import type { BranchList } from '../api/types'

/** The branch a merge should target by default: the one checked out, else main or master, else the first. */
export function defaultMergeTarget(list: BranchList | undefined): string {
  if (!list || list.branches.length === 0) return ''
  if (list.current && list.branches.includes(list.current)) return list.current
  return list.branches.find((b) => b === 'main' || b === 'master') ?? list.branches[0]
}

/** The branch to bring into the target by default: the one checked out when it is not the target, else the first other. */
export function defaultMergeSource(list: BranchList | undefined, target: string): string {
  const others = list?.branches.filter((b) => b !== target) ?? []
  if (list?.current && others.includes(list.current)) return list.current
  return others[0] ?? ''
}

/** Why nothing can be merged into the target right now, said before trying; null when it can. */
export function mergeBlocker(list: BranchList | undefined, target: string): string | null {
  const changes = list?.uncommittedChanges ?? 0
  if (!list || target !== list.current || changes === 0) return null
  return `${target} is checked out with ${changes} uncommitted ${changes === 1 ? 'change' : 'changes'}: nothing can be merged into it until you commit or stash them. Or choose another branch.`
}

/** How a branch stands against its remote, in words, with whether a push makes sense now. */
export function remoteStanding(list: BranchList | undefined, branch: string): { text: string; canPush: boolean } | null {
  const tracking = list?.tracking?.find((t) => t.branch === branch)
  if (!list?.remotes?.length) return null
  if (!tracking?.upstream) return { text: `${branch} is not on ${list.remotes[0]} yet.`, canPush: true }
  if (tracking.behind > 0)
    return { text: `${tracking.upstream} has ${tracking.behind} commit${tracking.behind === 1 ? '' : 's'} that ${branch} lacks: pull first (git pull). Openspace never forces a push.`, canPush: false }
  if (tracking.ahead > 0) return { text: `${branch} is ${tracking.ahead} commit${tracking.ahead === 1 ? '' : 's'} ahead of ${tracking.upstream}.`, canPush: true }
  return { text: `${branch} is up to date with ${tracking.upstream}, as of the last fetch.`, canPush: false }
}
