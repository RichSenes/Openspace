import { useState, type FormEvent } from 'react'
import { ChevronDown, ChevronUp, CircleCheck, CircleX, LoaderCircle, Pencil, Play, Plus, SkipForward, Square, Trash2 } from 'lucide-react'
import {
  useAskFirst,
  useCreateRoutine,
  useDecideProposal,
  useDeleteRoutine,
  useGoogle,
  useKanjiCourse,
  useKnownKanji,
  useProposals,
  useRoutineKinds,
  useRoutineRuns,
  useRoutines,
  useRunRoutine,
  useStopRoutine,
  useTurnOnRoutine,
  useUpdateRoutine,
} from '../api/hooks'
import type { OfficeView, RoutineRunStatus, RoutineView, ScheduleView, Weekday } from '../api/types'
import { Markdown } from '../reports/Markdown'
import { useRoutineProgress } from '../realtime/routineProgress'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import { asksForKanji, GRADES, gradeLabel, readKanji, withKanji } from './kanji'
import { readText, withText } from './settings'
import { describeSchedule, RUN_LABEL, scheduleFrom, settingsProblem, toggleDay, WEEK, whenNear } from './schedule'
import './routines.css'

const RUN_ICON: Record<RoutineRunStatus, typeof CircleCheck> = { Running: LoaderCircle, Succeeded: CircleCheck, Failed: CircleX }

/** Recurring jobs of an office: what they proposed, when they run, what their runs did. */
export function RoutinesPanel({ office }: { office: OfficeView }) {
  const routines = useRoutines(office.id)
  const kinds = useRoutineKinds()
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<RoutineView>()

  return (
    <section className="routines">
      <header className="routines-head">
        <div>
          <h1>Routines of {office.name}</h1>
          <p className="hint">Jobs that start on their own, at the times you choose.</p>
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
        <p className="hint">No routines in this office yet: New routine makes the first one.</p>
      ) : (
        <ul className="routine-list" aria-label="Routines">
          {routines.data?.map((r) => (
            <RoutineCard key={r.id} routine={r} onEdit={() => setEditing(r)} />
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
        {waiting.length} {waiting.length === 1 ? 'proposal waits' : 'proposals wait'} for your yes
      </h2>
      {waiting.map((p) => (
        <article key={p.id} className="proposal">
          <header>
            <h3>{p.title}</h3>
            <span className="hint">
              {p.routineName} · {whenNear(p.createdAt)}
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

/** One routine: whether it is on, when it runs next, how its last run went, and what can be done with it. */
function RoutineCard({ routine, onEdit }: { routine: RoutineView; onEdit: () => void }) {
  const run = useRunRoutine()
  const stop = useStopRoutine()
  const running = useRoutineProgress().has(routine.id) || routine.lastRun?.status === 'Running'
  const turnOn = useTurnOnRoutine()
  const askFirst = useAskFirst()
  const remove = useDeleteRoutine()
  const toast = useToast()
  const [deleting, setDeleting] = useState(false)
  const [history, setHistory] = useState(false)
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
        <button
          type="button"
          role="switch"
          aria-checked={routine.enabled}
          aria-label={`${routine.name} runs at its times`}
          className="switch"
          disabled={turnOn.isPending}
          onClick={() => turnOn.mutate({ id: routine.id, enabled: !routine.enabled })}
        >
          <span className="switch-track" aria-hidden>
            <span className="switch-knob" />
          </span>
          {routine.enabled ? 'On' : 'Off'}
        </button>
      </header>

      <dl className="routine-facts">
        <div>
          <dt>Next run</dt>
          <dd>{!routine.enabled ? 'None: it is off' : routine.nextRunAt ? whenNear(routine.nextRunAt) : '—'}</dd>
        </div>
        <div>
          <dt>Last run</dt>
          <dd>
            {last && Icon ? (
              <>
                <span className={`run-state run-${last.status.toLowerCase()}`}>
                  <Icon size={14} aria-hidden /> {RUN_LABEL[last.status]}
                  {last.status === 'Running' ? ', started ' : ' '}
                  {whenNear(last.startedAt)}
                </span>
                {last.summary && <span className="run-summary">{last.summary}</span>}
              </>
            ) : (
              'It has not run yet'
            )}
          </dd>
        </div>
      </dl>

      {asksForKanji(routine.kind, routine.settings) && <KanjiCourse routineId={routine.id} />}

      <fieldset className="routine-acting">
        <legend>Before it touches your things</legend>
        <div className="choice" role="group">
          <button type="button" className="btn" aria-pressed={!routine.askFirst} disabled={askFirst.isPending} onClick={() => askFirst.mutate({ id: routine.id, askFirst: false })}>
            {!routine.askFirst && <CircleCheck size={14} aria-hidden />} Asks only when it can&apos;t be undone
          </button>
          <button type="button" className="btn" aria-pressed={routine.askFirst} disabled={askFirst.isPending} onClick={() => askFirst.mutate({ id: routine.id, askFirst: true })}>
            {routine.askFirst && <CircleCheck size={14} aria-hidden />} Always asks first
          </button>
        </div>
        <p className="hint">
          {routine.askFirst
            ? 'Everything it wants to do waits for your yes at the top of this page.'
            : 'Sorting or archiving it does by itself and writes in its history; deleting, replying or applying waits for your yes.'}
        </p>
      </fieldset>

      <div className="routine-actions">
        {running ? (
          <button type="button" className="btn" disabled={stop.isPending} onClick={() => stop.mutate(routine.id, { onSuccess: () => toast('Stopped: the routine keeps its schedule') })}>
            <Square size={16} /> Stop
          </button>
        ) : (
          <button type="button" className="btn" disabled={run.isPending} onClick={() => run.mutate(routine.id, { onSuccess: () => toast('It runs in a moment') })}>
            <Play size={16} /> Run now
          </button>
        )}
        <button type="button" className="btn" onClick={onEdit}>
          <Pencil size={16} /> Change
        </button>
        <button type="button" className="btn" aria-expanded={history} onClick={() => setHistory(!history)}>
          {history ? <ChevronUp size={16} /> : <ChevronDown size={16} />} History
        </button>
        <span className="routine-actions-end">
          {deleting ? (
            <>
              <button type="button" className="btn btn-quiet" onClick={() => setDeleting(false)}>
                Keep it
              </button>
              <button type="button" className="btn btn-danger" onClick={() => remove.mutate(routine.id, { onSuccess: () => toast('Routine deleted') })}>
                <Trash2 size={16} /> Delete it with its history
              </button>
            </>
          ) : (
            <button type="button" className="btn btn-quiet" onClick={() => setDeleting(true)}>
              <Trash2 size={16} /> Delete…
            </button>
          )}
        </span>
      </div>
      {history && <Runs routineId={routine.id} />}
    </li>
  )
}

/** The kanji the paper explains next, with a way to skip the ones the person already knows. */
function KanjiCourse({ routineId }: { routineId: string }) {
  const course = useKanjiCourse(routineId, true)
  const known = useKnownKanji()
  const toast = useToast()
  const data = course.data
  const next = data?.next

  return (
    <section className="kanji-course" aria-label="Kanji of the day">
      <h3>Kanji of the day</h3>
      {!data ? (
        course.error ? <p className="error">{course.error.message}</p> : <p className="hint">Reading the dictionaries…</p>
      ) : !data.available ? (
        <p>
          The dictionaries are missing from <code>{data.folder}</code>. Run <code>scripts/openspace kanji-data</code> to download them.
        </p>
      ) : (
        <div className="kanji-next">
          {next ? (
            <>
              <span lang="ja" className="kanji-glyph" aria-hidden>
                {next.literal}
              </span>
              <div className="kanji-text">
                <p>
                  Next issue explains <strong lang="ja">{next.literal}</strong>, &ldquo;{next.meanings.slice(0, 3).join(', ')}&rdquo;
                </p>
                <p className="hint">
                  Taught in {gradeLabel(next.grade)}. {data.explained} explained so far, {data.known} skipped because you know them.
                </p>
                <button
                  type="button"
                  className="btn"
                  disabled={known.isPending}
                  onClick={() => known.mutate({ id: routineId, kanji: next.literal }, { onSuccess: () => toast(`${next.literal} skipped: the next issue explains another one`) })}
                >
                  <SkipForward size={16} /> I know {next.literal}, skip it
                </button>
              </div>
            </>
          ) : (
            <p>
              Every kanji of the list is done: {data.explained} explained, {data.known} you already knew.
            </p>
          )}
        </div>
      )}
    </section>
  )
}

function Runs({ routineId }: { routineId: string }) {
  const runs = useRoutineRuns(routineId)
  if (runs.isPending) return <p className="hint">Reading the history…</p>
  if (!runs.data?.length) return <p className="hint">It has not run yet: its runs show up here, each with what it did.</p>
  return (
    <ul className="run-list" aria-label="History">
      {runs.data.map((r) => {
        const Icon = RUN_ICON[r.status]
        return (
          <li key={r.id}>
            <details>
              <summary>
                <span className={`run-state run-${r.status.toLowerCase()}`}>
                  <Icon size={14} aria-hidden /> {RUN_LABEL[r.status]}
                </span>
                <span>
                  {whenNear(r.startedAt)}
                  {r.byHand && ', started by hand'}
                </span>
                {r.summary && <span className="run-summary">{r.summary}</span>}
              </summary>
              <pre className="run-log">{r.log || 'Nothing written.'}</pre>
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
        <legend>Days</legend>
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
      <p className="hint">
        {describeSchedule(scheduleFrom(days, at, everyWeeks))}
        {days.length === 0 && '. Pick days to run only on those.'}
      </p>
      {kind === 'morning-paper' && <KanjiChoiceField settings={settings} onChange={setSettings} />}
      {kind === 'mail' && <AccountField settings={settings} onChange={setSettings} />}
      <details className="routine-advanced">
        <summary>Advanced settings (JSON)</summary>
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

/** The kanji of the day in a paper's form, written into its settings. */
function KanjiChoiceField({ settings, onChange }: { settings: string; onChange: (settings: string) => void }) {
  const choice = readKanji(settings)
  const broken = Boolean(settingsProblem(settings))
  return (
    <fieldset className="field routine-kanji" disabled={broken}>
      <legend>Kanji of the day</legend>
      <label className="check">
        <input type="checkbox" checked={choice.enabled} onChange={(e) => onChange(withKanji(settings, { ...choice, enabled: e.target.checked }))} />
        Close every issue with one kanji: readings, strokes, words and sentences
      </label>
      {choice.enabled && (
        <label className="field">
          <span>Start from</span>
          <select className="input" value={choice.fromGrade} onChange={(e) => onChange(withKanji(settings, { ...choice, fromGrade: Number(e.target.value) }))}>
            {GRADES.map((g) => (
              <option key={g} value={g}>
                {gradeLabel(g)}
              </option>
            ))}
          </select>
        </label>
      )}
      {broken && <p className="hint">Fix the advanced settings first.</p>}
    </fieldset>
  )
}

/** The Google account a mail routine works on, chosen among the connected ones. */
function AccountField({ settings, onChange }: { settings: string; onChange: (settings: string) => void }) {
  const google = useGoogle()
  const account = readText(settings, 'account')
  const accounts = google.data?.accounts ?? []
  const missing = account !== '' && !accounts.some((a) => a.email === account)
  return (
    <label className="field">
      <span>Google account</span>
      <select className="input" value={account} disabled={Boolean(settingsProblem(settings))} onChange={(e) => onChange(withText(settings, 'account', e.target.value))}>
        <option value="">Choose an account</option>
        {accounts.map((a) => (
          <option key={a.email} value={a.email}>
            {a.email}
          </option>
        ))}
        {missing && <option value={account}>{account} (not connected)</option>}
      </select>
      {google.data && accounts.length === 0 && <span className="hint">No account connected yet: connect one in Settings, under Google accounts.</span>}
    </label>
  )
}
