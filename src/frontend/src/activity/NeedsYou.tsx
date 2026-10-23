import { useEffect, useRef, useState } from 'react'
import { Hand } from 'lucide-react'
import { useActivity, useAnswerPermission, useOffices } from '../api/hooks'
import { useToast } from '../ui/toastContext'
import { waitingDesks } from './activity'
import './needsYou.css'

/**
 * Agents waiting for a person's answer, behind one button in the top bar. Nothing pops up
 * on its own: the button says how many wait, and the list opens when it is pressed.
 */
export function NeedsYou() {
  const waiting = waitingDesks(useActivity().data, useOffices().data)
  const [open, setOpen] = useState(false)
  const answer = useAnswerPermission()
  const toast = useToast()
  const box = useRef<HTMLDivElement>(null)
  const count = waiting.length

  // The list closes with Escape or a click anywhere else.
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    const onClick = (e: MouseEvent) => !box.current?.contains(e.target as Node) && setOpen(false)
    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onClick)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('mousedown', onClick)
    }
  }, [open])

  if (count === 0) return null

  return (
    <div className="needs-you" ref={box}>
      <button
        type="button"
        className="btn needs-you-button"
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => setOpen(!open)}
      >
        <Hand size={16} aria-hidden /> {count} {count === 1 ? 'needs' : 'need'} you
      </button>
      {open && (
        <section className="needs-you-panel" role="dialog" aria-label="Agents waiting for you">
          {waiting.map((w) => (
            <div key={w.taskId} className="needs-you-card">
              <p className="needs-you-text">
                <strong>
                  {w.officeName} · {w.deskName}
                </strong>
                <span>{w.request}</span>
                {w.detail && <code className="needs-you-detail mono">{w.detail}</code>}
              </p>
              <div className="needs-you-actions">
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
          <p className="hint">In auto mode only risky requests wait here. Change it in Settings.</p>
        </section>
      )}
    </div>
  )
}
