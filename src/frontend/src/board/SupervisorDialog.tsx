import { useState, type FormEvent } from 'react'
import { useApproveEpic, usePlan } from '../api/hooks'
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

/** Write a larger request; the supervisor reads the project and proposes tasks for the desks. */
export function SupervisorDialog({ office, desks, open, onClose, onShowEpic }: Props) {
  const [text, setText] = useState('')
  const plan = usePlan(office.id)
  const approve = useApproveEpic()
  const toast = useToast()
  const result = plan.data
  const withDesk = result?.tasks.filter((t) => t.deskId !== null).length ?? 0

  const close = () => {
    plan.reset()
    approve.reset()
    onClose()
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    plan.mutate(text, { onSuccess: () => setText('') })
  }

  const show = () => {
    if (result) onShowEpic(result.epic.id)
    close()
  }

  return (
    <Dialog title={`Ask the supervisor of ${office.name}`} open={open} onClose={close}>
      {!result ? (
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
            The supervisor reads the repository without changing it and proposes tasks for the desks of this office. Nothing starts until
            you approve.
          </p>
          {plan.isPending && <p className="hint">The supervisor is reading the project… this can take a minute.</p>}
          {plan.error && <p className="error">{plan.error.message}</p>}
          <div className="dialog-actions">
            <button type="button" className="btn btn-quiet" onClick={close}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={plan.isPending}>
              {plan.isPending ? 'Planning…' : 'Plan it'}
            </button>
          </div>
        </form>
      ) : (
        <form onSubmit={(e) => e.preventDefault()}>
          <h3 className="plan-title">{result.epic.title}</h3>
          <ol className="plan-list">
            {result.tasks.map((t) => {
              const desk = desks.find((d) => d.id === t.deskId)
              return (
                <li key={t.id}>
                  <span className="plan-task">{t.title}</span>
                  <span className={desk ? 'hint' : 'error'}>{desk ? `${desk.roomName} · ${desk.name}` : 'No matching desk: choose one when you approve it'}</span>
                </li>
              )
            })}
          </ol>
          {approve.error && <p className="error">{approve.error.message}</p>}
          <div className="dialog-actions">
            <button type="button" className="btn btn-quiet" onClick={show}>
              Review on the board
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={withDesk === 0 || approve.isPending}
              onClick={() =>
                approve.mutate(result.epic.id, {
                  onSuccess: ({ approved }) => {
                    toast(`${approved} ${approved === 1 ? 'task' : 'tasks'} queued`)
                    show()
                  },
                })
              }
            >
              Approve all {withDesk > 0 && `(${withDesk})`}
            </button>
          </div>
        </form>
      )}
    </Dialog>
  )
}
