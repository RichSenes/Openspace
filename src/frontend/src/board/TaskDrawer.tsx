import { useState, type FormEvent } from 'react'
import { X } from 'lucide-react'
import {
  useActivity,
  useAnswerPermission,
  useApproveTask,
  useBranches,
  useMergeTask,
  useTaskCommand,
  useTaskDiff,
  useTaskText,
} from '../api/hooks'
import { defaultMergeTarget } from './mergeTarget'
import { TaskLog } from './TaskLog'
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
  const answer = useAnswerPermission()
  const activity = useActivity().data?.find((a) => a.taskId === task.id)
  const [showDiff, setShowDiff] = useState(task.status === 'Review')
  const canDiff = task.branch !== null && (task.status === 'Running' || task.status === 'Review' || task.status === 'Failed')
  const diff = useTaskDiff(task.id, canDiff && showDiff)
  const canMerge = task.status === 'Done' && task.branch !== null && task.mergedInto === null
  const branches = useBranches(task.officeId, canMerge)
  const merge = useMergeTask()
  const [mergeTarget, setMergeTarget] = useState('')
  const target = mergeTarget || defaultMergeTarget(branches.data)
  const [approveDesk, setApproveDesk] = useState(task.deskId ?? '')
  const [feedback, setFeedback] = useState('')
  const [writingFeedback, setWritingFeedback] = useState(false)
  const [note, setNote] = useState('')
  const [tab, setTab] = useState<'overview' | 'log'>('overview')

  const actions = actionsFor(task.status)
  const desk = desks.find((d) => d.id === task.deskId)
  const deskName = (id: string | null) => desks.find((d) => d.id === id)?.name ?? 'an agent'
  const error = command.error ?? approve.error ?? sendBack.error ?? addNote.error ?? answer.error ?? merge.error

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

      <div className="drawer-tabs" role="tablist" aria-label="Task views">
        <button type="button" role="tab" aria-selected={tab === 'overview'} onClick={() => setTab('overview')}>
          Overview
        </button>
        <button type="button" role="tab" aria-selected={tab === 'log'} onClick={() => setTab('log')}>
          Log
        </button>
      </div>

      <div className="drawer-body">
        <h2>{task.title}</h2>
        {tab === 'log' ? (
          <TaskLog taskId={task.id} />
        ) : (
          <>
            <dl className="facts">
              <dt>Desk</dt>
              <dd>{desk ? `${desk.roomName} · ${desk.name} (${desk.agentName})` : 'None yet'}</dd>
              <dt>Asked by</dt>
              <dd>{task.requestedBy}</dd>
              <dt>Created</dt>
              <dd>{when(task.createdAt)}</dd>
              {task.mergedInto ? (
                <>
                  <dt>Merged into</dt>
                  <dd className="mono">{task.mergedInto}</dd>
                </>
              ) : (
                task.branch && (
                  <>
                    <dt>Branch</dt>
                    <dd className="mono">{task.branch}</dd>
                  </>
                )
              )}
            </dl>
            {task.description && <p className="description">{task.description}</p>}

            {task.status === 'Running' && activity && (
              <section className={`now now-${activity.kind.toLowerCase()}`} aria-label="What the agent is doing">
                <h3 className="pixel now-title">{activity.kind === 'Waiting' ? 'Needs you' : 'Now'}</h3>
                {activity.step && <p className="now-step">{activity.step}</p>}
                {activity.doing && <p className="hint">{activity.doing}</p>}
                {activity.kind === 'Waiting' && (
                  <div className="row">
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={answer.isPending}
                      onClick={() => answer.mutate({ taskId: task.id, allow: true }, { onSuccess: () => toast('Allowed once') })}
                    >
                      Allow once
                    </button>
                    <button
                      type="button"
                      className="btn btn-danger"
                      disabled={answer.isPending}
                      onClick={() => answer.mutate({ taskId: task.id, allow: false }, { onSuccess: () => toast('Refused') })}
                    >
                      Refuse
                    </button>
                  </div>
                )}
              </section>
            )}

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
            {task.procedure.length > 0 && (
          <section className="procedure" aria-label="Required procedure">
            <h3 className="pixel notes-title">
              Procedure · {task.procedure.filter((s) => s.completedAt).length} of {task.procedure.length} done
            </h3>
            <ol className="procedure-list">
              {task.procedure.map((s) => (
                <li key={s.number} className={s.completedAt ? 'step-done' : 'step-open'}>
                  <span className="step-mark" aria-hidden="true">
                    {s.completedAt ? '✓' : '○'}
                  </span>
                  <div>
                    <p>{s.text}</p>
                    {s.evidence ? <p className="hint">{s.evidence}</p> : <p className="hint">Not reported yet</p>}
                  </div>
                </li>
              ))}
            </ol>
          </section>
        )}

        {canMerge && (
              <section className="merge" aria-label="Merge">
                <label className="field">
                  <span>Bring this work into</span>
                  <select className="input mono" value={target} onChange={(e) => setMergeTarget(e.target.value)}>
                    {(branches.data?.branches ?? []).map((b) => (
                      <option key={b} value={b}>
                        {b}
                        {b === branches.data?.current ? ' (checked out)' : ''}
                      </option>
                    ))}
                  </select>
                </label>
                <div className="row">
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={!target || merge.isPending}
                    onClick={() => merge.mutate({ taskId: task.id, target }, { onSuccess: () => toast(`Merged into ${target}`) })}
                  >
                    Merge into {target || '…'}
                  </button>
                </div>
                <p className="hint">
                  Your checkout never moves: other branches are merged in a temporary copy. With uncommitted changes or
                  conflicts nothing is touched.
                </p>
              </section>
            )}

            {error && <p className="error">{error.message}</p>}

            {canDiff && (
              <section className="changes" aria-label="Changes">
                <div className="changes-head">
                  <h3 className="pixel notes-title">Changes</h3>
                  <button type="button" className="btn btn-quiet" onClick={() => setShowDiff(!showDiff)}>
                    {showDiff ? 'Hide changes' : 'Show changes'}
                  </button>
                </div>
                {showDiff &&
                  (diff.isPending ? (
                    <p className="hint">Reading the changes…</p>
                  ) : diff.data ? (
                    <pre className="diff mono">
                      {diff.data.split('\n').map((line, i) => (
                        <span key={i} className={diffClass(line)}>
                          {line}
                          {'\n'}
                        </span>
                      ))}
                    </pre>
                  ) : (
                    <p className="hint">No changes yet.</p>
                  ))}
              </section>
            )}

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
          </>
        )}
      </div>
    </aside>
  )
}

function diffClass(line: string): string | undefined {
  if (line.startsWith('+++') || line.startsWith('---') || line.startsWith('diff ') || line.startsWith('index ')) return 'diff-meta'
  if (line.startsWith('@@')) return 'diff-hunk'
  if (line.startsWith('+')) return 'diff-add'
  if (line.startsWith('-')) return 'diff-del'
  return undefined
}
