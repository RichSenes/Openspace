import type { SupervisorJob } from '../api/types'

/** The plans of one office, running first, then the newest finished ones. */
export function jobsOf(jobs: SupervisorJob[] | undefined, officeId: string): SupervisorJob[] {
  return (jobs ?? [])
    .filter((j) => j.officeId === officeId)
    .sort((a, b) => (a.state === 'Planning') === (b.state === 'Planning') ? b.startedAt.localeCompare(a.startedAt) : a.state === 'Planning' ? -1 : 1)
}

/** The plan running for one thing, such as one audit run's findings. */
export function runningFor(jobs: SupervisorJob[] | undefined, key: string): SupervisorJob | undefined {
  return jobs?.find((j) => j.key === key && j.state === 'Planning')
}

/** What a finished plan made, in a few words. */
export function outcomeOf(job: SupervisorJob): string {
  if (job.state === 'Failed') return `Could not plan: ${job.error ?? 'the supervisor gave no reason'}`
  if (job.state === 'Planning') return 'The supervisor is reading the project…'
  if (job.tasks === 0) return 'Nothing to propose'
  const tasks = job.tasks === 1 ? '1 task' : `${job.tasks} tasks`
  return job.epicId ? `${tasks} proposed at the desks` : `${tasks} proposed without a desk: choose one for each`
}

/** Plans that finished since the last look: what was planning before and is not any more. */
export function justEnded(before: Map<string, SupervisorJob['state']>, now: SupervisorJob[]): SupervisorJob[] {
  return now.filter((j) => j.state !== 'Planning' && before.get(j.id) === 'Planning')
}
