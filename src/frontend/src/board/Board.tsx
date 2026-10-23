import { useMemo, useState } from 'react'
import { Check, Plus, Sparkles, TriangleAlert } from 'lucide-react'
import { useEpics, useOfficeTasks } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { StatusPill } from '../ui/StatusPill'
import { desksOf } from './deskLookup'
import { NewTaskDialog } from './NewTaskDialog'
import { TaskDrawer } from './TaskDrawer'
import { SupervisorDialog } from './SupervisorDialog'
import { COLUMNS, filterByEpic, groupByColumn, reviewVerdictOf, type EpicFilter } from './taskRules'
import './board.css'

export function Board({ office }: { office: OfficeView }) {
  const tasks = useOfficeTasks(office.id)
  const [creating, setCreating] = useState(false)
  const [planning, setPlanning] = useState(false)
  const [epicFilter, setEpicFilter] = useState<EpicFilter>('all')
  const epics = useEpics(office.id)
  const epicTitle = (id: string | null) => epics.data?.find((e) => e.id === id)?.title
  const [openTaskId, setOpenTaskId] = useState<string>()
  const desks = useMemo(() => desksOf(office), [office])
  const groups = useMemo(() => groupByColumn(filterByEpic(tasks.data ?? [], epicFilter)), [tasks.data, epicFilter])
  const openTask = tasks.data?.find((t) => t.id === openTaskId)

  return (
    <section className="board">
      <header className="board-head">
        <h1>{office.name}</h1>
        <div className="row">
          {(epics.data?.length ?? 0) > 0 && (
            <label>
              <span className="sr-only">Show</span>
              <select className="input" value={epicFilter} onChange={(e) => setEpicFilter(e.target.value)}>
                <option value="all">All work</option>
                {epics.data?.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.title}
                  </option>
                ))}
                <option value="none">Tasks outside any request</option>
              </select>
            </label>
          )}
          <button type="button" className="btn" onClick={() => setPlanning(true)}>
            <Sparkles size={16} /> Ask the supervisor
          </button>
          <button type="button" className="btn btn-primary" onClick={() => setCreating(true)}>
            <Plus size={16} /> New task
          </button>
        </div>
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
                          {epicTitle(task.epicId) && <span className="card-epic hint">{epicTitle(task.epicId)}</span>}
                          <span className="card-title">{task.title}</span>
                          <span className="card-meta hint">
                            {desk ? `${desk.roomName} · ${desk.name}` : 'No desk yet'}
                            {task.notes.length > 0 &&
                              ` · ${task.notes.length} ${task.notes.length === 1 ? 'note' : 'notes'}`}
                          </span>
                          {reviewVerdictOf(task) === 'ok' && (
                            <span className="card-review card-review-ok">
                              <Check size={12} aria-hidden /> Claude: ready to accept
                            </span>
                          )}
                          {reviewVerdictOf(task) === 'changes' && (
                            <span className="card-review card-review-changes">
                              <TriangleAlert size={12} aria-hidden /> Claude: changes suggested
                            </span>
                          )}
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
      <SupervisorDialog
        office={office}
        desks={desks}
        open={planning}
        onClose={() => setPlanning(false)}
        onShowEpic={(id) => setEpicFilter(id)}
      />
      {openTask && <TaskDrawer task={openTask} desks={desks} onClose={() => setOpenTaskId(undefined)} />}
    </section>
  )
}
