import { useState, type FormEvent } from 'react'
import { CircleCheck, CircleX, LoaderCircle, Play, Plus, Trash2 } from 'lucide-react'
import {
  useCreateRoutine,
  useDecideProposal,
  useDeleteRoutine,
  useProposals,
  useRoutineKinds,
  useRoutineRuns,
  useRoutines,
  useRunRoutine,
  useTurnOnRoutine,
  useUpdateRoutine,
} from '../api/hooks'
import type { OfficeView, RoutineRunStatus, RoutineView, ScheduleView, Weekday } from '../api/types'
import { Markdown } from '../reports/Markdown'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import { describeSchedule, RUN_LABEL, scheduleFrom, settingsProblem, toggleDay, WEEK } from './schedule'
import './routines.css'

const when = (iso: string | null) =>
  iso ? new Date(iso).toLocaleString(undefined, { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '—'

const RUN_ICON: Record<RoutineRunStatus, typeof CircleCheck> = { Running: LoaderCircle, Succeeded: CircleCheck, Failed: CircleX }

/** Recurring jobs of an office: what they proposed, when they run, what their runs did. */
export function RoutinesPanel({ office }: { office: OfficeView }) {
  const routines = useRoutines(office.id)
  const kinds = useRoutineKinds()
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<RoutineView>()
  const [openId, setOpenId] = useState<string>()

  return (
    <section className="routines">
      <header className="routines-head">
        <div>
          <h1>Routines of {office.name}</h1>
          <p className="hint">Jobs that run on their own at set times. They propose; nothing touches your things until you say yes.</p>
        </div>
        <button type="button" className="btn btn-primary" onClick={() => setCreating(true)} disabled={!kinds.data?.length}>
          <Plus size={16} /> New routine
        </button>
      </header>
      {kinds.data?.length === 0 && (
        <p className="hint">No kinds of routine are available yet: the morning paper, the mail and the job offers arrive with the next updates.</p>
      )}

      <Proposals officeId={office.id} />

      {routines.data?.length === 0 ? (
        <p className="hint">No routines in this office yet.</p>
      ) : (
        <ul className="routine-list" aria-label="Routines">
          {routines.data?.map((r) => (
            <RoutineCard key={r.id} routine={r} open={openId === r.id} onToggleRuns={() => setOpenId(openId === r.id ? undefined : r.id)} onEdit={() => setEditing(r)} />
          ))}
        </ul>
      )}

      <Dialog title={`New routine in ${office.name}`} open={creating} onClose={() => setCreating(false)}>
        <RoutineForm officeId={office.id} onDone={() => setCreating(false)} />
      </Dialog>
      <Dialog title={editing ? `Change ${editing.name}` : 'Change routine'} open={Boolean(editing)} onClose={() => setEditing(undefined)}>
        {editing && <RoutineForm officeId={office.id} routine={editing} onDone={() => setEditing(undefined)} />}
      </Dialog>
    </section>
  )
}

function Proposals({ officeId }: { officeId: string }) {
  const proposals = useProposals(officeId)
  const decide = useDecideProposal()
  const toast = useToast()
  const waiting = proposals.data ?? []
  if (waiting.length === 0) return null

  const answer = (id: string, approve: boolean) =>
    decide.mutate({ id, approve }, { onSuccess: () => toast(approve ? 'Approved: the routine carries it out' : 'Turned down') })

  return (
    <section className="proposals" aria-label="Proposals waiting for you">
      <h2>
        {waiting.length} {waiting.length === 1 ? 'proposal waits' : 'proposals wait'} for you
      </h2>
      {waiting.map((p) => (
        <article key={p.id} className="proposal">
          <header>
            <h3>{p.title}</h3>
            <span className="hint">
              {p.routineName} · {when(p.createdAt)}
            </span>
          </header>
          <Markdown text={p.body} />
          <div className="row">
            <button type="button" className="btn btn-primary" disabled={decide.isPending} onClick={() => answer(p.id, true)}>
              Yes, go ahead
            </button>
            <button type="button" className="btn" disabled={decide.isPending} onClick={() => answer(p.id, false)}>
              No
            </button>
          </div>
        </article>
      ))}
      {decide.error && <p className="error">{decide.error.message}</p>}
    </section>
  )
}

function RoutineCard({ routine, open, onToggleRuns, onEdit }: { routine: RoutineView; open: boolean; onToggleRuns: () => void; onEdit: () => void }) {
  const run = useRunRoutine()
  const turnOn = useTurnOnRoutine()
  const remove = useDeleteRoutine()
  const toast = useToast()
  const [deleting, setDeleting] = useState(false)
  const last = routine.lastRun
  const Icon = last ? RUN_ICON[last.status] : undefined

  return (
    <li className="routine">
      <header className="routine-head">
        <div>
          <h2>{routine.name}</h2>
          <p className="hint">
            {routine.kindName} · {describeSchedule(routine.schedule)}
          </p>
        </div>
        <label className="routine-switch">
          <input
            type="checkbox"
            checked={routine.enabled}
            onChange={(e) => turnOn.mutate({ id: routine.id, enabled: e.target.checked })}
          />
          <span>{routine.enabled ? 'On' : 'Off'}</span>
        </label>
      </header>
      <dl className="routine-facts">
        <div>
          <dt>Next run</dt>
          <dd>{routine.enabled ? when(routine.nextRunAt) : 'Turned off'}</dd>
        </div>
        <div>
          <dt>Last run</dt>
          <dd>
            {last && Icon ? (
              <span className={`run-state run-${last.status.toLowerCase()}`}>
                <Icon size={14} aria-hidden /> {RUN_LABEL[last.status]} {when(last.startedAt)}
                {last.summary && <span className="run-summary">: {last.summary}</span>}
              </span>
            ) : (
              'Never'
            )}
          </dd>
        </div>
      </dl>
      <div className="row">
        <button type="button" className="btn" disabled={run.isPending} onClick={() => run.mutate(routine.id, { onSuccess: () => toast('It runs in a moment') })}>
          <Play size={16} /> Run now
        </button>
        <button type="button" className="btn" onClick={onEdit}>
          Change
        </button>
        <button type="button" className="btn" aria-expanded={open} onClick={onToggleRuns}>
          {open ? 'Hide runs' : 'Runs'}
        </button>
        {deleting ? (
          <>
            <button type="button" className="btn btn-danger" onClick={() => remove.mutate(routine.id, { onSuccess: () => toast('Routine deleted') })}>
              <Trash2 size={16} /> Delete it, with its runs
            </button>
            <button type="button" className="btn btn-quiet" onClick={() => setDeleting(false)}>
              Keep it
            </button>
          </>
        ) : (
          <button type="button" className="btn btn-quiet" onClick={() => setDeleting(true)}>
            Delete…
          </button>
        )}
      </div>
      {open && <Runs routineId={routine.id} />}
    </li>
  )
}

function Runs({ routineId }: { routineId: string }) {
  const runs = useRoutineRuns(routineId)
  if (runs.isPending) return <p className="hint">Reading the runs…</p>
  if (!runs.data?.length) return <p className="hint">It has not run yet.</p>
  return (
    <ul className="run-list" aria-label="Runs">
      {runs.data.map((r) => {
        const Icon = RUN_ICON[r.status]
        return (
          <li key={r.id}>
            <details>
              <summary>
                <span className={`run-state run-${r.status.toLowerCase()}`}>
                  <Icon size={14} aria-hidden /> {RUN_LABEL[r.status]}
                </span>{' '}
                {when(r.startedAt)}
                {r.byHand && ' · by hand'}
                {r.summary && <span className="run-summary"> · {r.summary}</span>}
              </summary>
              <pre className="run-log">{r.log || 'Nothing logged.'}</pre>
            </details>
          </li>
        )
      })}
    </ul>
  )
}

function RoutineForm({ officeId, routine, onDone }: { officeId: string; routine?: RoutineView; onDone: () => void }) {
  const kinds = useRoutineKinds()
  const create = useCreateRoutine(officeId)
  const update = useUpdateRoutine()
  const toast = useToast()
  const [kind, setKind] = useState(routine?.kind ?? kinds.data?.[0]?.key ?? '')
  const chosen = kinds.data?.find((k) => k.key === kind)
  const [name, setName] = useState(routine?.name ?? chosen?.name ?? '')
  const [days, setDays] = useState<Weekday[]>(routine?.schedule.days ?? [])
  const [at, setAt] = useState(routine?.schedule.at ?? '07:00')
  const [everyWeeks, setEveryWeeks] = useState(routine?.schedule.everyWeeks ?? 1)
  const [settings, setSettings] = useState(routine?.settings ?? chosen?.defaultSettings ?? '{}')
  const problem = settingsProblem(settings)
  const error = create.error ?? update.error

  const pickKind = (key: string) => {
    const next = kinds.data?.find((k) => k.key === key)
    setKind(key)
    if (next) {
      setSettings(next.defaultSettings)
      if (!name.trim()) setName(next.name)
    }
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const schedule: ScheduleView = scheduleFrom(days, at, everyWeeks)
    if (routine) {
      update.mutate({ id: routine.id, name, schedule, settings }, { onSuccess: () => { toast('Routine saved'); onDone() } })
    } else {
      create.mutate({ name, kind, schedule, settings }, { onSuccess: () => { toast('Routine created'); onDone() } })
    }
  }

  return (
    <form className="routine-form" onSubmit={submit}>
      {!routine && (
        <label className="field">
          <span>What it does</span>
          <select className="input" value={kind} onChange={(e) => pickKind(e.target.value)} required>
            {kinds.data?.map((k) => (
              <option key={k.key} value={k.key}>
                {k.name}
              </option>
            ))}
          </select>
          {chosen && <span className="hint">{chosen.description}</span>}
        </label>
      )}
      <label className="field">
        <span>Name</span>
        <input className="input" value={name} onChange={(e) => setName(e.target.value)} maxLength={80} required />
      </label>
      <fieldset className="field routine-days">
        <legend>Days (none chosen: every day)</legend>
        <div className="row">
          {WEEK.map((w) => (
            <button
              key={w.day}
              type="button"
              className="btn day-toggle"
              aria-pressed={days.includes(w.day)}
              onClick={() => setDays(toggleDay(days, w.day))}
            >
              {days.includes(w.day) && <CircleCheck size={14} aria-hidden />} {w.short}
            </button>
          ))}
        </div>
      </fieldset>
      <div className="row">
        <label className="field">
          <span>At</span>
          <input className="input" type="time" value={at} onChange={(e) => setAt(e.target.value)} required />
        </label>
        <label className="field">
          <span>Every</span>
          <select className="input" value={days.length === 0 ? 1 : everyWeeks} disabled={days.length === 0} onChange={(e) => setEveryWeeks(Number(e.target.value))}>
            {[1, 2, 3, 4].map((n) => (
              <option key={n} value={n}>
                {n === 1 ? 'week' : `${n} weeks`}
              </option>
            ))}
          </select>
        </label>
      </div>
      <p className="hint">{describeSchedule(scheduleFrom(days, at, everyWeeks))}</p>
      <details className="routine-advanced">
        <summary>Settings (JSON)</summary>
        <textarea className="input" rows={8} value={settings} onChange={(e) => setSettings(e.target.value)} spellCheck={false} />
        {problem && <p className="error">{problem}</p>}
      </details>
      {error && <p className="error">{error.message}</p>}
      <div className="row">
        <button type="submit" className="btn btn-primary" disabled={Boolean(problem) || create.isPending || update.isPending || (!routine && !kind)}>
          {routine ? 'Save' : 'Create routine'}
        </button>
        <button type="button" className="btn btn-quiet" onClick={onDone}>
          Cancel
        </button>
      </div>
    </form>
  )
}
