import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { useOfficeTasks } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { StatusPill } from '../ui/StatusPill'
import { desksOf } from './deskLookup'
import { NewTaskDialog } from './NewTaskDialog'
import { TaskDrawer } from './TaskDrawer'
import { COLUMNS, groupByColumn } from './taskRules'
import './board.css'

export function Board({ office }: { office: OfficeView }) {
  const tasks = useOfficeTasks(office.id)
  const [creating, setCreating] = useState(false)
  const [openTaskId, setOpenTaskId] = useState<string>()
  const desks = useMemo(() => desksOf(office), [office])
  const groups = useMemo(() => groupByColumn(tasks.data ?? []), [tasks.data])
  const openTask = tasks.data?.find((t) => t.id === openTaskId)

  return (
    <section className="board">
      <header className="board-head">
        <h1>{office.name}</h1>
        <button type="button" className="btn btn-primary" onClick={() => setCreating(true)}>
          <Plus size={16} /> New task
        </button>
      </header>

      {tasks.error && <p className="error board-error">Could not load tasks: {tasks.error.message}</p>}

      <div className="columns">
        {COLUMNS.map((column) => {
          const items = groups.get(column.status) ?? []
          return (
            <section key={column.status} className="column" aria-label={column.label}>
              <header className="column-head">
                <StatusPill status={column.status} />
                <span className="hint">{items.length}</span>
              </header>
              {items.length === 0 ? (
                <p className="hint column-empty">{column.emptyHint}</p>
              ) : (
                <ul className="cards">
                  {items.map((task) => {
                    const desk = desks.find((d) => d.id === task.deskId)
                    return (
                      <li key={task.id}>
                        <button
                          type="button"
                          className="card"
                          aria-current={task.id === openTaskId ? 'true' : undefined}
                          onClick={() => setOpenTaskId(task.id)}
                        >
                          <span className="card-title">{task.title}</span>
                          <span className="card-meta hint">
                            {desk ? `${desk.roomName} · ${desk.name}` : 'No desk yet'}
                            {task.notes.length > 0 &&
                              ` · ${task.notes.length} ${task.notes.length === 1 ? 'note' : 'notes'}`}
                          </span>
                        </button>
                      </li>
                    )
                  })}
                </ul>
              )}
            </section>
          )
        })}
      </div>

      <NewTaskDialog office={office} desks={desks} open={creating} onClose={() => setCreating(false)} />
      {openTask && <TaskDrawer task={openTask} desks={desks} onClose={() => setOpenTaskId(undefined)} />}
    </section>
  )
}
