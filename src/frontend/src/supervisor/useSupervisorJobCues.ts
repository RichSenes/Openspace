import { useEffect, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { keys, useOffices, useSupervisorJobs } from '../api/hooks'
import type { SupervisorJob } from '../api/types'
import { emitCue } from '../sounds/rules'
import { useToast } from '../ui/toastContext'
import { justEnded, outcomeOf } from './supervisorJobs'

/**
 * Wherever you are in Openspace, says when a supervisor's plan ends, and brings the new
 * tasks in: the plan ran in the background while you did something else.
 */
export function useSupervisorJobCues() {
  const jobs = useSupervisorJobs().data
  const offices = useOffices().data
  const client = useQueryClient()
  const toast = useToast()
  const seen = useRef<Map<string, SupervisorJob['state']> | null>(null)

  useEffect(() => {
    if (!jobs) return
    const before = seen.current
    seen.current = new Map(jobs.map((j) => [j.id, j.state]))
    if (!before) return
    const ended = justEnded(before, jobs)
    if (ended.length === 0) return
    void client.invalidateQueries({ queryKey: keys.tasks })
    void client.invalidateQueries({ queryKey: keys.epics })
    void client.invalidateQueries({ queryKey: ['security-audits'] })
    for (const job of ended) {
      const office = offices?.find((o) => o.id === job.officeId)?.name
      toast(`${office ? `${office}: ` : ''}${job.what}${job.what.endsWith('…') ? '' : '.'} ${outcomeOf(job)}${job.state === 'Done' && job.tasks > 0 ? ': approve them on the Board' : ''}`)
      if (job.state === 'Done' && job.tasks > 0) emitCue({ event: 'plan-ready', officeId: job.officeId, key: `plan:${job.id}` })
    }
  }, [jobs, offices, client, toast])
}
