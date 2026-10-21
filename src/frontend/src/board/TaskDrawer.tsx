import { useState, type FormEvent } from 'react'
import { X } from 'lucide-react'
import { useApproveTask, useTaskCommand, useTaskText } from '../api/hooks'
import type { WorkTaskView } from '../api/types'
import { StatusPill } from '../ui/StatusPill'
import { useToast } from '../ui/toastContext'
import type { DeskWithRoom } from './deskLookup'
import { actionsFor } from './taskRules'

interface Props {
  task: WorkTaskView
  desks: DeskWithRoom[]
  onClose: () => void
}

const when = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })

export function TaskDrawer({ task, desks, onClose }: Props) {
  const toast = useToast()
  const command = useTaskCommand()
  const approve = useApproveTask()
  const sendBack = useTaskText('send-back')
  const addNote = useTaskText('notes')
  const [approveDesk, setApproveDesk] = useState(task.deskId ?? '')
  const [feedback, setFeedback] = useState('')
  const [writingFeedback, setWritingFeedback] = useState(false)
  const [note, setNote] = useState('')

  const actions = actionsFor(task.status)
  const desk = desks.find((d) => d.id === task.deskId)
  const deskName = (id: string | null) => desks.find((d) => d.id === id)?.name ?? 'an agent'
  const error = command.error ?? approve.error ?? sendBack.error ?? addNote.error

  const run = (name: 'accept' | 'retry' | 'discard', done: string) =>
    command.mutate({ taskId: task.id, command: name }, { onSuccess: () => toast(done) })

  const submitFeedback = (e: FormEvent) => {
    e.preventDefault()
    sendBack.mutate(
      { taskId: task.id, text: feedback },
      {
        onSuccess: () => {
          toast('Sent back to the queue')
          setFeedback('')
          setWritingFeedback(false)
        },
      },
    )
  }

  const submitNote = (e: FormEvent) => {
    e.preventDefault()
    addNote.mutate({ taskId: task.id, text: note }, { onSuccess: () => setNote('') })
  }

  return (
    <aside className="drawer" aria-label={`Task: ${task.title}`}>
      <header className="drawer-head">
        <StatusPill status={task.status} />
        <button type="button" className="btn btn-quiet" onClick={onClose} aria-label="Close task">
          <X size={16} />
        </button>
      </header>

      <div className="drawer-body">
        <h2>{task.title}</h2>
        <dl className="facts">
          <dt>Desk</dt>
          <dd>{desk ? `${desk.roomName} · ${desk.name} (${desk.agentName})` : 'None yet'}</dd>
          <dt>Asked by</dt>
          <dd>{task.requestedBy}</dd>
          <dt>Created</dt>
          <dd>{when(task.createdAt)}</dd>
        </dl>
        {task.description && <p className="description">{task.description}</p>}

        {actions.length > 0 && (
          <div className="drawer-actions">
            {actions.includes('approve') && (
              <div className="approve">
                <label className="field">
                  <span>Desk that will do it</span>
                  <select className="input" value={approveDesk} onChange={(e) => setApproveDesk(e.target.value)}>
                    <option value="">Choose a desk</option>
                    {desks.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.roomName} · {d.name}
                      </option>
                    ))}
                  </select>
                </label>
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={!approveDesk || approve.isPending}
                  onClick={() =>
                    approve.mutate(
                      { taskId: task.id, deskId: approveDesk },
                      { onSuccess: () => toast('Task approved and queued') },
                    )
                  }
                >
                  Approve and queue
                </button>
              </div>
            )}
            <div className="row">
              {actions.includes('accept') && (
                <button type="button" className="btn btn-primary" onClick={() => run('accept', 'Work accepted')}>
                  Accept work
                </button>
              )}
              {actions.includes('send-back') && !writingFeedback && (
                <button type="button" className="btn" onClick={() => setWritingFeedback(true)}>
                  Send back with feedback
                </button>
              )}
              {actions.includes('retry') && (
                <button type="button" className="btn btn-primary" onClick={() => run('retry', 'Back in the queue')}>
                  Try again
                </button>
              )}
              {actions.includes('discard') && (
                <button type="button" className="btn btn-danger" onClick={() => run('discard', 'Task discarded')}>
                  Discard task
                </button>
              )}
            </div>
            {writingFeedback && (
              <form className="stack" onSubmit={submitFeedback}>
                <label className="field">
                  <span>What should change</span>
                  <textarea className="input" value={feedback} onChange={(e) => setFeedback(e.target.value)} required autoFocus />
                </label>
                <div className="row">
                  <button type="submit" className="btn btn-primary" disabled={sendBack.isPending}>
                    Send back
                  </button>
                  <button type="button" className="btn btn-quiet" onClick={() => setWritingFeedback(false)}>
                    Cancel
                  </button>
                </div>
              </form>
            )}
          </div>
        )}
        {error && <p className="error">{error.message}</p>}

        <section className="notes" aria-label="Notes">
          <h3 className="pixel notes-title">Notes</h3>
          {task.notes.length === 0 ? (
            <p className="hint">No notes yet. Agents will log what they do here.</p>
          ) : (
            <ol className="note-list">
              {task.notes.map((n, i) => (
                <li key={i} className={`note note-${n.authorKind.toLowerCase()}`}>
                  <span className="hint">
                    {n.authorKind === 'Desk' ? deskName(n.deskId) : (n.authorName ?? 'Openspace')} · {when(n.writtenAt)}
                  </span>
                  <p>{n.text}</p>
                </li>
              ))}
            </ol>
          )}
          {task.status !== 'Discarded' && (
            <form className="stack" onSubmit={submitNote}>
              <label className="field">
                <span>Add a note</span>
                <textarea className="input" value={note} onChange={(e) => setNote(e.target.value)} required />
              </label>
              <div className="row">
                <button type="submit" className="btn" disabled={addNote.isPending}>
                  Add note
                </button>
              </div>
            </form>
          )}
        </section>
      </div>
    </aside>
  )
}
