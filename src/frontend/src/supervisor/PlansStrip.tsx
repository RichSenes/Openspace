import { useState } from 'react'
import { Check, Hourglass, TriangleAlert, X } from 'lucide-react'
import { useApproveEpic, useSupervisorJobs } from '../api/hooks'
import { useToast } from '../ui/toastContext'
import { jobsOf, outcomeOf } from './supervisorJobs'
import './supervisor.css'

const at = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })

/**
 * The supervisor's plans for this office, on top of the Board: those running, and those
 * that ended in the last quarter of an hour, with their tasks to look at or approve.
 */
export function PlansStrip({ officeId, onShowEpic }: { officeId: string; onShowEpic: (epicId: string) => void }) {
  const jobs = jobsOf(useSupervisorJobs().data, officeId)
  const approve = useApproveEpic()
  const toast = useToast()
  const [hidden, setHidden] = useState<string[]>([])
  const shown = jobs.filter((j) => !hidden.includes(j.id))
  if (shown.length === 0) return null

  return (
    <ul className="plans-strip" aria-label="The supervisor's plans">
      {shown.map((job) => (
        <li key={job.id} className={`plan-job plan-job-${job.state.toLowerCase()}`}>
          {job.state === 'Planning' ? <Hourglass size={16} aria-hidden /> : job.state === 'Done' ? <Check size={16} aria-hidden /> : <TriangleAlert size={16} aria-hidden />}
          <div className="plan-job-text">
            <span className="plan-job-what">{job.what}</span>
            <span className="hint">
              {outcomeOf(job)} · asked at {at(job.startedAt)}
            </span>
          </div>
          {job.state === 'Done' && job.epicId && job.tasks > 0 && (
            <div className="row">
              <button type="button" className="btn" onClick={() => onShowEpic(job.epicId!)}>
                Show them
              </button>
              <button
                type="button"
                className="btn btn-primary"
                disabled={approve.isPending}
                onClick={() =>
                  approve.mutate(job.epicId!, {
                    onSuccess: ({ approved }) => {
                      toast(`${approved} ${approved === 1 ? 'task' : 'tasks'} queued`)
                      setHidden((h) => [...h, job.id])
                    },
                  })
                }
              >
                Approve all
              </button>
            </div>
          )}
          {job.state !== 'Planning' && (
            <button type="button" className="btn btn-quiet plan-job-close" aria-label="Hide" onClick={() => setHidden((h) => [...h, job.id])}>
              <X size={16} />
            </button>
          )}
        </li>
      ))}
    </ul>
  )
}
