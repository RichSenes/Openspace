import { ChevronRight, ListTodo, Palette, Plus, X } from 'lucide-react'
import { useAnswerPermission } from '../api/hooks'
import type { DeskActivity, DeskContext, Intern, WorkTaskView } from '../api/types'
import type { DeskWithRoom } from '../board/deskLookup'
import { ENGINE_LABEL } from '../engines/engines'
import { StatusPill } from '../ui/StatusPill'
import { useToast } from '../ui/toastContext'
import { Portrait } from './LookDialog'
import { answersFor } from '../activity/activity'
import { energyWords } from '../activity/energy'

interface Props {
  desk: DeskWithRoom
  theme: string
  activity: DeskActivity | undefined
  /** How full the agent's conversation is. */
  context?: DeskContext
  /** Helpers working for the agent right now. */
  interns?: readonly Intern[]
  /** The desk's tasks, oldest first. */
  tasks: readonly WorkTaskView[]
  /** Where on the building it opens, in pixels from its corner. */
  style: React.CSSProperties
  onClose: () => void
  onOpenTask: (taskId: string) => void
  onNewTask: () => void
  onLook: () => void
}

const OPEN: WorkTaskView['status'][] = ['Running', 'Queued', 'Review', 'Failed', 'Proposed']

/** What an agent is up to and what you can do with it, opened by clicking it in the building. */
export function AgentCard({ desk, theme, activity, context, interns, tasks, style, onClose, onOpenTask, onNewTask, onLook }: Props) {
  const answer = useAnswerPermission()
  const toast = useToast()
  const current = tasks.find((t) => t.id === activity?.taskId && t.status === 'Running') ?? tasks.find((t) => t.status === 'Running')
  const open = tasks.filter((t) => t.id !== current?.id && OPEN.includes(t.status))
  const done = tasks
    .filter((t) => t.status === 'Done')
    .toSorted((a, b) => b.statusChangedAt.localeCompare(a.statusChangedAt))
    .slice(0, 3)
  const waiting = activity?.kind === 'Waiting' && activity.taskId === current?.id

  return (
    <section className="agent-card" style={style} role="dialog" aria-label={`${desk.name}, ${desk.roomName}`}>
      <header className="agent-card-head">
        <Portrait desk={desk} theme={theme} />
        <div>
          <h2>{desk.name}</h2>
          <p className="hint">
            {desk.roomName} · {ENGINE_LABEL[desk.engine ?? 'OpenCode']}
            {desk.model ? ` · ${desk.model}` : ''}
          </p>
        </div>
        <button type="button" className="btn btn-quiet" onClick={onClose} aria-label="Close">
          <X size={16} />
        </button>
      </header>

      {current ? (
        <div className="agent-card-now">
          <TaskRow task={current} onOpen={onOpenTask} />
          {activity?.taskId === current.id && activity.headline && <p className={waiting ? 'agent-card-ask' : 'hint'}>{activity.headline}</p>}
          {context?.taskId === current.id && energyWords(context) && <p className="hint">Energy: {energyWords(context)}</p>}
          {interns && interns.length > 0 && (
            <div>
              <p className="hint">{interns.length === 1 ? 'An intern is helping:' : `${interns.length} interns are helping:`}</p>
              <ul className="agent-card-interns">
                {interns.map((i) => (
                  <li key={i.id}>{i.what}</li>
                ))}
              </ul>
            </div>
          )}
          {waiting && activity.detail && <code className="mono agent-card-detail">{activity.detail}</code>}
          {waiting && (
            <div className="row">
              <button
                type="button"
                className="btn btn-primary"
                disabled={answer.isPending}
                onClick={() => answer.mutate({ taskId: current.id, allow: true }, { onSuccess: () => toast(`${desk.name}: ${answersFor(activity.question).saidYes}`) })}
              >
                {answersFor(activity.question).yes}
              </button>
              <button
                type="button"
                className="btn btn-danger"
                disabled={answer.isPending}
                onClick={() => answer.mutate({ taskId: current.id, allow: false }, { onSuccess: () => toast(`${desk.name}: ${answersFor(activity.question).saidNo}`) })}
              >
                {answersFor(activity.question).no}
              </button>
            </div>
          )}
        </div>
      ) : (
        <p className="hint">Not working on anything right now.</p>
      )}

      {open.length > 0 && (
        <section className="agent-card-list" aria-label="Its other tasks">
          <h3 className="pixel agent-card-label">Also on its desk</h3>
          <ul className="agent-card-tasks">
            {open.slice(0, 4).map((t) => (
              <li key={t.id}>
                <TaskRow task={t} onOpen={onOpenTask} />
              </li>
            ))}
            {open.length > 4 && <li className="hint">and {open.length - 4} more on the Board</li>}
          </ul>
        </section>
      )}

      {done.length > 0 && (
        <section className="agent-card-list" aria-label="Done lately">
          <h3 className="pixel agent-card-label">Done lately</h3>
          <ul className="agent-card-tasks">
            {done.map((t) => (
              <li key={t.id}>
                <TaskRow task={t} onOpen={onOpenTask} />
              </li>
            ))}
          </ul>
        </section>
      )}

      {answer.error && <p className="error">{answer.error.message}</p>}
      <div className="agent-card-actions">
        <button type="button" className="btn" onClick={onNewTask}>
          <Plus size={16} aria-hidden /> Give a task
        </button>
        <button type="button" className="btn" disabled={!current} onClick={() => current && onOpenTask(current.id)}>
          <ListTodo size={16} aria-hidden /> Open task
        </button>
        <button type="button" className="btn" onClick={onLook}>
          <Palette size={16} aria-hidden /> Look
        </button>
      </div>
    </section>
  )
}

/** A task of the desk; clicking it opens the task on the Board. */
function TaskRow({ task, onOpen }: { task: WorkTaskView; onOpen: (taskId: string) => void }) {
  return (
    <button type="button" className="agent-card-task" onClick={() => onOpen(task.id)} title="Open on the Board">
      <StatusPill status={task.status} />
      <span className="agent-card-task-title">{task.title}</span>
      <ChevronRight size={14} aria-hidden />
    </button>
  )
}
