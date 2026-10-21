import { useEffect, useState } from 'react'
import { useActivity, useAnswerPermission, useOffices } from '../api/hooks'
import { useToast } from '../ui/toastContext'
import { waitingDesks, type WaitingDesk } from './activity'
import { mergeForExit, type Shown } from './exitList'
import './needsYou.css'

/** Matches the pop-out animation in needsYou.css. */
const EXIT_MS = 180

/**
 * Cards that pop up from the bottom while agents wait for a person's answer,
 * one per waiting desk, and pop out once answered.
 */
export function NeedsYou() {
  const waiting = waitingDesks(useActivity().data, useOffices().data)
  const signature = waiting.map((w) => `${w.taskId}|${w.officeName}|${w.deskName}|${w.request}`).join('\n')
  const [shown, setShown] = useState<Shown<WaitingDesk>[]>(() => mergeForExit([], waiting, (w) => w.taskId))
  const [seen, setSeen] = useState(signature)
  const answer = useAnswerPermission()
  const toast = useToast()

  // When the waiting list changes, adjust what is on screen during render (React's
  // pattern for state derived from changing input); vanished cards start leaving.
  if (seen !== signature) {
    setSeen(signature)
    setShown((current) => mergeForExit(current, waiting, (w) => w.taskId))
  }

  // Leaving cards are dropped once their pop-out animation is over.
  useEffect(() => {
    if (!shown.some((s) => s.leaving)) return
    const timer = setTimeout(() => setShown((current) => current.filter((s) => !s.leaving)), EXIT_MS)
    return () => clearTimeout(timer)
  }, [shown])

  if (shown.length === 0) return null

  return (
    <section className="needs-you" aria-label="Agents waiting for you" aria-live="polite">
      {shown.map(({ key, item: w, leaving }) => (
        <div key={key} className={`needs-you-card${leaving ? ' leaving' : ''}`}>
          <p className="needs-you-text">
            <span className="pixel needs-you-tag">Needs you</span>
            <strong>
              {w.officeName} · {w.deskName}
            </strong>
            <span>{w.request}</span>
          </p>
          <div className="needs-you-actions">
            <button
              type="button"
              className="btn btn-primary"
              disabled={answer.isPending || leaving}
              onClick={() => answer.mutate({ taskId: w.taskId, allow: true }, { onSuccess: () => toast(`${w.deskName}: allowed once`) })}
            >
              Allow once
            </button>
            <button
              type="button"
              className="btn btn-danger"
              disabled={answer.isPending || leaving}
              onClick={() => answer.mutate({ taskId: w.taskId, allow: false }, { onSuccess: () => toast(`${w.deskName}: refused`) })}
            >
              Refuse
            </button>
          </div>
        </div>
      ))}
      {answer.error && <p className="error needs-you-error">{answer.error.message}</p>}
    </section>
  )
}
