import { useState } from 'react'
import { Check, Hourglass } from 'lucide-react'
import { useSetDependencies } from '../api/hooks'
import type { WorkTaskView } from '../api/types'
import { useToast } from '../ui/toastContext'
import { isDoneForOthers, standing } from './taskRules'

interface Props {
  task: WorkTaskView
  /** Every task of the office, to show and choose from. */
  tasks: readonly WorkTaskView[]
}

/** What a task builds on: it starts once each of those is accepted and merged. Changed until it starts. */
export function BuildsOn({ task, tasks }: Props) {
  const dependsOn = task.dependsOn ?? []
  const [editing, setEditing] = useState(false)
  const [chosen, setChosen] = useState<ReadonlySet<string>>(new Set(dependsOn))
  const save = useSetDependencies()
  const toast = useToast()
  const editable = task.status === 'Proposed' || task.status === 'Queued' || task.status === 'Failed'
  const listed = dependsOn.map((id) => tasks.find((t) => t.id === id)).filter((t): t is WorkTaskView => t !== undefined)
  const candidates = tasks.filter((t) => t.id !== task.id && t.status !== 'Discarded')

  if (listed.length === 0 && !editable) return null

  const toggle = (id: string) =>
    setChosen((s) => {
      const next = new Set(s)
      if (!next.delete(id)) next.add(id)
      return next
    })

  const submit = () =>
    save.mutate(
      { taskId: task.id, taskIds: [...chosen] },
      {
        onSuccess: () => {
          setEditing(false)
          toast(chosen.size ? `Waits for ${chosen.size} ${chosen.size === 1 ? 'task' : 'tasks'}` : 'Waits for nothing now')
        },
      },
    )

  return (
    <section className="builds-on" aria-label="Builds on">
      <div className="changes-head">
        <h3 className="pixel notes-title">Builds on</h3>
        {editable && !editing && (
          <button type="button" className="btn btn-quiet" onClick={() => { setChosen(new Set(dependsOn)); setEditing(true) }}>
            Change
          </button>
        )}
      </div>
      {!editing &&
        (listed.length === 0 ? (
          <p className="hint">Nothing: it can start as soon as a seat is free.</p>
        ) : (
          <ul className="builds-on-list">
            {listed.map((t) => (
              <li key={t.id} className={isDoneForOthers(t) ? 'builds-on-done' : 'builds-on-waiting'}>
                <span aria-hidden="true">{isDoneForOthers(t) ? <Check size={14} /> : <Hourglass size={14} />}</span>
                <span>
                  {t.title} <span className="hint">· {standing(t)}</span>
                </span>
              </li>
            ))}
          </ul>
        ))}
      {editing && (
        <div className="stack">
          <p className="hint">It starts once every task ticked here is accepted and merged, so its agent works on their result.</p>
          <ul className="builds-on-pick">
            {candidates.map((t) => (
              <li key={t.id}>
                <label className="check-row">
                  <input type="checkbox" checked={chosen.has(t.id)} onChange={() => toggle(t.id)} />
                  <span>
                    {t.title} <span className="hint">· {standing(t)}</span>
                  </span>
                </label>
              </li>
            ))}
          </ul>
          <div className="row">
            <button type="button" className="btn btn-primary" disabled={save.isPending} onClick={submit}>
              Save
            </button>
            <button type="button" className="btn btn-quiet" onClick={() => setEditing(false)}>
              Cancel
            </button>
          </div>
          {save.error && <p className="error">{save.error.message}</p>}
        </div>
      )}
    </section>
  )
}
