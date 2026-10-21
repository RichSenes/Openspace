import { useActivity, useAnswerPermission, useOffices } from '../api/hooks'
import { useToast } from '../ui/toastContext'
import { waitingDesks } from './activity'
import './needsYou.css'

/** Stays on top of every view while an agent waits for a person's answer. */
export function NeedsYou() {
  const waiting = waitingDesks(useActivity().data, useOffices().data)
  const answer = useAnswerPermission()
  const toast = useToast()

  if (waiting.length === 0) return null

  return (
    <section className="needs-you" aria-label="Agents waiting for you">
      {waiting.map((w) => (
        <div key={w.taskId} className="needs-you-item">
          <p>
            <span className="pixel needs-you-tag">Needs you</span>
            <strong>
              {w.officeName} · {w.deskName}
            </strong>{' '}
            {w.request}
          </p>
          <div className="row">
            <button
              type="button"
              className="btn btn-primary"
              disabled={answer.isPending}
              onClick={() => answer.mutate({ taskId: w.taskId, allow: true }, { onSuccess: () => toast(`${w.deskName}: allowed once`) })}
            >
              Allow once
            </button>
            <button
              type="button"
              className="btn btn-danger"
              disabled={answer.isPending}
              onClick={() => answer.mutate({ taskId: w.taskId, allow: false }, { onSuccess: () => toast(`${w.deskName}: refused`) })}
            >
              Refuse
            </button>
          </div>
        </div>
      ))}
      {answer.error && <p className="error">{answer.error.message}</p>}
    </section>
  )
}
