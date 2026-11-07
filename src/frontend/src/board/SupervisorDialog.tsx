import { useState, type FormEvent } from 'react'
import { usePlan } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import type { DeskWithRoom } from './deskLookup'

interface Props {
  office: OfficeView
  desks: DeskWithRoom[]
  open: boolean
  onClose: () => void
  onShowEpic: (epicId: string) => void
}

/**
 * Write a larger request; the supervisor reads the project and proposes tasks for the desks.
 * The plan runs in the background: the dialog closes at once, and the plan shows on top of
 * the Board, and in the status bar, until it is ready.
 */
export function SupervisorDialog({ office, open, onClose }: Props) {
  const [text, setText] = useState('')
  const plan = usePlan(office.id)
  const toast = useToast()

  const close = () => {
    plan.reset()
    onClose()
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    plan.mutate(text, {
      onSuccess: () => {
        setText('')
        toast('The supervisor is planning: keep working, its tasks appear on top of the Board')
        close()
      },
    })
  }

  return (
    <Dialog title={`Ask the supervisor of ${office.name}`} open={open} onClose={close}>
      <form onSubmit={submit}>
        <label className="field">
          <span>What do you need? Write it as you would to a team lead.</span>
          <textarea
            className="input"
            rows={6}
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="e.g. Add a contact page with a form that emails the site admin, and make sure it is checked before release."
            required
            autoFocus
            disabled={plan.isPending}
          />
        </label>
        <p className="hint">
          The supervisor reads the repository without changing it and proposes tasks for the desks of this office. It works in the
          background: nothing starts until you approve.
        </p>
        {plan.error && <p className="error">{plan.error.message}</p>}
        <div className="dialog-actions">
          <button type="button" className="btn btn-quiet" onClick={close}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary" disabled={plan.isPending}>
            Plan it
          </button>
        </div>
      </form>
    </Dialog>
  )
}
