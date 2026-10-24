import { useMemo, useState } from 'react'
import { usePersistentState } from '../usePersistentState'
import { Check, GitMerge, Hourglass, ListChecks, Plus, Search, Sparkles, TriangleAlert } from 'lucide-react'
import { useEngines, useEpics, useOfficeTasks } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { StatusPill } from '../ui/StatusPill'
import { desksOf } from './deskLookup'
import { pauseOf } from '../engines/engines'
import { NewTaskDialog } from './NewTaskDialog'
import { TaskDrawer } from './TaskDrawer'
import { SupervisorDialog } from './SupervisorDialog'
import { MergeDialog } from './MergeDialog'
import { awaitingMerge, checksDone, COLUMNS, searchTasks, withoutMerged, filterByEpic, groupByColumn, reviewVerdictOf, type EpicFilter } from './taskRules'
import './board.css'

interface Props {
  office: OfficeView
  /** A task to open at once, e.g. picked from an agent in the building. */
  initialTaskId?: string
}

export function Board({ office, initialTaskId }: Props) {
  const tasks = useOfficeTasks(office.id)
  const [creating, setCreating] = useState(false)
  const [planning, setPlanning] = useState(false)
  const [merging, setMerging] = useState(false)
  const [epicFilter, setEpicFilter] = useState<EpicFilter>('all')
  const epics = useEpics(office.id)
  const epicTitle = (id: string | null) => epics.data?.find((e) => e.id === id)?.title
  const [openTaskId, setOpenTaskId] = useState<string | undefined>(initialTaskId)
  const engines = useEngines().data
  const reviewerOn = Boolean(engines?.reviewer)

  const desks = useMemo(() => desksOf(office), [office])
  const [query, setQuery] = useState('')
  const [showMerged, setShowMerged] = usePersistentState<'yes' | 'no'>('openspace.show-merged', 'no')
  const { shown, hidden } = useMemo(() => {
    const found = searchTasks(filterByEpic(tasks.data ?? [], epicFilter), query)
    return showMerged === 'yes' ? { shown: found, hidden: 0 } : withoutMerged(found)
  }, [tasks.data, epicFilter, query, showMerged])
  const groups = useMemo(() => groupByColumn(shown), [shown])
  const openTask = tasks.data?.find((t) => t.id === openTaskId)
  const toMerge = awaitingMerge(tasks.data ?? []).length

  return (
    <section className={openTask ? 'board has-drawer' : 'board'}>
      <div className="board-main">
      <header className="board-head">
        <h1>{office.name}</h1>
        <div className="row">
          <label className="board-search">
            <Search size={14} aria-hidden />
            <span className="sr-only">Search tasks</span>
            <input className="input" type="search" placeholder="Search tasks" value={query} onChange={(e) => setQuery(e.target.value)} />
          </label>
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
          <button type="button" className="btn" onClick={() => setMerging(true)}>
            <GitMerge size={16} /> Merge desk{toMerge > 0 && <span className="count-badge">{toMerge}</span>}
          </button>
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
          // With a task open, empty columns fold to a strip so the others keep their width.
          const folded = Boolean(openTask) && items.length === 0
          return (
            <section key={column.status} className={folded ? 'column column-folded' : 'column'} aria-label={column.label}>
              <header className="column-head">
                <StatusPill status={column.status} />
                <span className="hint">{items.length}</span>
              </header>
              {folded ? null : items.length === 0 ? (
                <p className="hint column-empty">{column.emptyHint}</p>
              ) : (
                <ul className="cards">
                  {items.map((task) => {
                    const desk = desks.find((d) => d.id === task.deskId)
                    const verdict = reviewVerdictOf(task, reviewerOn)
                    const checks = checksDone(task)
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
                          {task.round > 1 && <span className="card-meta hint">Round {task.round}</span>}
                          {task.status === 'Queued' && pauseOf(engines, desk) && (
                            <span className="card-review">
                              <Hourglass size={12} aria-hidden /> Paused until{' '}
                              {new Date(pauseOf(engines, desk)!.until).toLocaleString(undefined, { weekday: 'short', hour: '2-digit', minute: '2-digit' })}:{' '}
                              {pauseOf(engines, desk)!.label} over their limit
                            </span>
                          )}
                          {task.status === 'Done' && task.branch !== null && (
                            <span className="card-meta hint">
                              {task.mergedInto ? `In ${task.mergedInto}` : 'Not merged yet'}
                            </span>
                          )}
                          {verdict === 'reviewing' && (
                            <span className="card-review">
                              <Hourglass size={12} aria-hidden /> Claude is reviewing…
                            </span>
                          )}
                          {verdict === 'ok' && (
                            <span className="card-review card-review-ok">
                              <Check size={12} aria-hidden /> Claude: ready to accept
                            </span>
                          )}
                          {verdict === 'changes' && (
                            <span className="card-review card-review-changes">
                              <TriangleAlert size={12} aria-hidden /> Claude: changes suggested
                            </span>
                          )}
                          {checks.total > 0 && (task.status === 'Review' || task.status === 'Done') && (
                            <span className="card-review">
                              <ListChecks size={12} aria-hidden /> Tried by hand: {checks.done} of {checks.total}
                            </span>
                          )}
                        </button>
                      </li>
                    )
                  })}
                </ul>
              )}
              {!folded && column.status === 'Done' && (hidden > 0 || showMerged === 'yes') && (
                <button type="button" className="btn btn-quiet column-more" onClick={() => setShowMerged(showMerged === 'yes' ? 'no' : 'yes')}>
                  {showMerged === 'yes' ? 'Hide merged work' : `Show ${hidden} merged`}
                </button>
              )}
            </section>
          )
        })}
      </div>

      </div>

      <NewTaskDialog office={office} desks={desks} open={creating} onClose={() => setCreating(false)} />
      <MergeDialog office={office} tasks={tasks.data ?? []} desks={desks} open={merging} onClose={() => setMerging(false)} />
      <SupervisorDialog
        office={office}
        desks={desks}
        open={planning}
        onClose={() => setPlanning(false)}
        onShowEpic={(id) => setEpicFilter(id)}
      />
      {openTask && <TaskDrawer key={openTask.id} task={openTask} desks={desks} reviewerOn={reviewerOn} onClose={() => setOpenTaskId(undefined)} />}
    </section>
  )
}
