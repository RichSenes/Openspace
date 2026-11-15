import { useRef, useState } from 'react'
import { ArrowDown, ArrowUp, LoaderCircle, Plus, Trash2 } from 'lucide-react'
import { useCreateRoutine, useRoutineChannels, useRoutineModels, useTestBoard, useUpdateRoutine, type FeedTest } from '../api/hooks'
import type { RoutineView, Weekday } from '../api/types'
import { useToast } from '../ui/toastContext'
import { Choice, Field, Lines, Part, ScheduleFields, TestResult } from './editorParts'
import { LANGUAGES, clamp, useFollowIndex } from './editorTools'
import { BOARD_KINDS, JAPANESE_LEVELS, boardName, jobsProblems, readJobs, writeJobs, yen, type Board, type Jobs } from './jobs'
import { move, wordsOf } from './paper'
import { scheduleFrom, settingsProblem } from './schedule'
import './paperEditor.css'

const PARTS = [
  { id: 'boards', label: 'Where to look' },
  { id: 'wants', label: 'What you look for' },
  { id: 'you', label: 'Who you are' },
  { id: 'judge', label: 'How to judge' },
  { id: 'delivery', label: 'Delivery' },
  { id: 'when', label: 'Name and time' },
  { id: 'advanced', label: 'Advanced (JSON)' },
] as const

/**
 * The job offers routine on a page of its own: where it looks, each place with a test; what the
 * person looks for and what they do not want; how offers are judged and where they arrive. The
 * JSON stays at the end, for what has no field yet.
 */
export function JobsEditor({ officeId, routine, defaults, onDone }: { officeId: string; routine?: RoutineView; defaults: string; onDone: () => void }) {
  const create = useCreateRoutine(officeId)
  const update = useUpdateRoutine()
  const toast = useToast()
  const [settings, setSettings] = useState(routine?.settings ?? defaults)
  const [name, setName] = useState(routine?.name ?? 'Job offers')
  const [days, setDays] = useState<Weekday[]>(routine?.schedule.days ?? [])
  const [at, setAt] = useState(routine?.schedule.at ?? '08:30')
  const [everyWeeks, setEveryWeeks] = useState(routine?.schedule.everyWeeks ?? 1)
  const [part, setPart] = useState<string>('boards')
  const body = useRef<HTMLDivElement>(null)

  const jobs = readJobs(settings)
  const problems = jobs ? jobsProblems(jobs) : [settingsProblem(settings) ?? 'The advanced settings are not valid JSON.']
  const change = (next: (j: Jobs) => Jobs) => jobs && setSettings(writeJobs(next(jobs)))
  const error = create.error ?? update.error
  useFollowIndex(body, jobs !== undefined, setPart)

  const save = () => {
    if (!jobs || problems.length > 0) return
    const schedule = scheduleFrom(days, at, everyWeeks)
    const done = (what: string) => () => {
      toast(what)
      onDone()
    }
    if (routine) update.mutate({ id: routine.id, name, schedule, settings }, { onSuccess: done('Job offers saved') })
    else create.mutate({ name, kind: 'job-offers', schedule, settings }, { onSuccess: done('Job offers created') })
  }

  const go = (id: string) => {
    setPart(id)
    document.getElementById(`paper-part-${id}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  return (
    <div className="paper-editor-room">
      <section className="paper-editor no-preview" aria-label={routine ? `Change ${routine.name}` : 'New job offers'}>
        <header className="paper-editor-head">
          <div>
            <p className="hint">Routines › {routine ? 'Change' : 'New'}</p>
            <h1>{jobs?.title || name}</h1>
          </div>
          <div className="paper-editor-actions">
            <button type="button" className="btn btn-quiet" onClick={onDone}>
              Cancel
            </button>
            <button type="button" className="btn btn-primary" onClick={save} disabled={problems.length > 0 || create.isPending || update.isPending}>
              {routine ? 'Save' : 'Create'}
            </button>
          </div>
          {(problems.length > 0 || error) && (
            <div className="paper-editor-problems" role="status">
              {problems.length > 0 && <p className="hint">Before saving:</p>}
              <ul>
                {problems.map((p) => (
                  <li key={p} className="error">
                    {p}
                  </li>
                ))}
                {error && <li className="error">{error.message}</li>}
              </ul>
            </div>
          )}
        </header>

        <nav className="paper-index" aria-label="Parts of the job offers' settings">
          {PARTS.map((p) => (
            <button key={p.id} type="button" aria-current={part === p.id ? 'true' : undefined} onClick={() => go(p.id)}>
              {p.label}
            </button>
          ))}
        </nav>

        <div className="paper-form" ref={body}>
          {jobs ? (
            <>
              <Part id="boards" title="Where to look">
                <p className="hint">
                  Each place is read every run; Test reads it once now. For a site Openspace may not read, paste the whole offer in the bot&apos;s chat:
                  it comes back judged, with its buttons (in a group, answer one of the bot&apos;s messages, or turn its privacy off with BotFather).
                </p>
                <div className="paper-sources">
                  {jobs.boards.map((board, i) => (
                    <BoardRow
                      key={i}
                      board={board}
                      first={i === 0}
                      last={i === jobs.boards.length - 1}
                      onChange={(patch) => change((j) => ({ ...j, boards: j.boards.map((b, k) => (k === i ? { ...b, ...patch } : b)) }))}
                      onMove={(by) => change((j) => ({ ...j, boards: move(j.boards, i, by) }))}
                      onRemove={() => change((j) => ({ ...j, boards: j.boards.filter((_, k) => k !== i) }))}
                    />
                  ))}
                </div>
                <div>
                  <button
                    type="button"
                    className="btn"
                    onClick={() => change((j) => ({ ...j, boards: [...j.boards, { kind: 'linkedin', value: '', allInJapan: false }] }))}
                  >
                    <Plus size={14} /> Add a place
                  </button>
                </div>
              </Part>

              <Part id="wants" title="What you look for">
                <div className="paper-grid">
                  <Lines
                    label="Roles"
                    hint="One per line: a title must hold one of these words. Empty takes every role."
                    value={jobs.roles}
                    onChange={(roles) => change((j) => ({ ...j, roles }))}
                  />
                  <Lines
                    label="What you do not want"
                    hint="One per line, in your words (Go as main language, QA): offers mainly about these get two stars at most and stay away."
                    value={jobs.avoid}
                    onChange={(avoid) => change((j) => ({ ...j, avoid }))}
                  />
                </div>
                <Field label="Cities you prefer" hint="Separated by commas, the first first: their offers come first in the chat.">
                  <input className="input" value={jobs.cities.join(', ')} onChange={(e) => change((j) => ({ ...j, cities: wordsOf(e.target.value) }))} />
                </Field>
                <Choice
                  label="Japanese, at most"
                  hint="Offers asking for more are left out. What a board does not say, the judge reads in the text."
                  value={jobs.japaneseUpTo}
                  options={JAPANESE_LEVELS}
                  onChange={(japaneseUpTo) => change((j) => ({ ...j, japaneseUpTo }))}
                />
                <div className="paper-grid">
                  <div className="field">
                    <label className="check">
                      <input type="checkbox" checked={jobs.visa} onChange={(e) => change((j) => ({ ...j, visa: e.target.checked }))} />
                      You need a work visa
                    </label>
                    <span className="hint">Offers that say they do not sponsor one, or take only people already in Japan, are left out.</span>
                  </div>
                  <Field label="Lowest yearly salary" hint={`In yen a year: ${yen(jobs.minSalary)}. Offers stating a lower top are left out; 0 for none.`}>
                    <input
                      className="input"
                      type="number"
                      min={0}
                      step={500000}
                      value={jobs.minSalary}
                      onChange={(e) => change((j) => ({ ...j, minSalary: clamp(e.target.value, 0, 100_000_000, 0) }))}
                    />
                  </Field>
                </div>
              </Part>

              <Part id="you" title="Who you are">
                <Field label="Your profile" hint="A text file on this computer with who you are, what you did and what you look for: the judge and the CV read it, nothing else does.">
                  <input className="input mono" value={jobs.profile} onChange={(e) => change((j) => ({ ...j, profile: e.target.value }))} />
                </Field>
                <div className="paper-grid">
                  <Field label="Offers you pasted" hint="Where offers you paste wait to be judged, one file each.">
                    <input className="input mono" value={jobs.inbox} onChange={(e) => change((j) => ({ ...j, inbox: e.target.value }))} />
                  </Field>
                  <Field label="Offers you want" hint="Where an offer you said yes to goes, in a folder of its own, with its CV.">
                    <input className="input mono" value={jobs.offers} onChange={(e) => change((j) => ({ ...j, offers: e.target.value }))} />
                  </Field>
                </div>
              </Part>

              <Part id="judge" title="How to judge">
                <div className="paper-grid">
                  <Field label="Offers judged per run" hint="Of the new ones, the desk picks this many for a full reading: more takes the model longer.">
                    <select className="input" value={jobs.most} onChange={(e) => change((j) => ({ ...j, most: Number(e.target.value) }))}>
                      {[4, 6, 8, 10, 12, 15, 20].map((n) => (
                        <option key={n} value={n}>
                          {n}
                        </option>
                      ))}
                      {![4, 6, 8, 10, 12, 15, 20].includes(jobs.most) && <option value={jobs.most}>{jobs.most}</option>}
                    </select>
                  </Field>
                  <JudgeModel jobs={jobs} onChange={change} />
                </div>
                <Choice
                  label="Sent from"
                  hint="The least fit, in stars, that reaches you."
                  value={jobs.fit}
                  options={[1, 2, 3, 4, 5].map((n) => ({ value: n, label: `${'★'.repeat(n)}` }))}
                  onChange={(fit) => change((j) => ({ ...j, fit }))}
                />
              </Part>

              <Part id="delivery" title="Delivery">
                <div className="paper-grid">
                  <Field label="Title" hint="On top of each run's message.">
                    <input className="input" value={jobs.title} onChange={(e) => change((j) => ({ ...j, title: e.target.value }))} />
                  </Field>
                  <Field label="Language" hint="Of the messages and of the judge's explanations.">
                    <select className="input" value={jobs.language} onChange={(e) => change((j) => ({ ...j, language: e.target.value }))}>
                      {[...new Set([...LANGUAGES, jobs.language])].map((l) => (
                        <option key={l}>{l}</option>
                      ))}
                    </select>
                  </Field>
                </div>
                <Channel jobs={jobs} onChange={change} />
              </Part>
            </>
          ) : (
            <p className="error">The advanced settings are not valid JSON: fix them at the end of the page to see the fields again.</p>
          )}

          <Part id="when" title="Name and time">
            <ScheduleFields
              name={name}
              onName={setName}
              nameHint="How the routine is called in Openspace; the title of its messages is above."
              at={at}
              onAt={setAt}
              days={days}
              onDays={setDays}
              everyWeeks={everyWeeks}
              onEveryWeeks={setEveryWeeks}
              doing="look"
            />
          </Part>

          <Part id="advanced" title="Advanced (JSON)">
            <p className="hint">Every setting as the routine keeps it. Changes here show in the fields above.</p>
            <textarea
              className="input mono paper-json"
              rows={14}
              value={settings}
              onChange={(e) => setSettings(e.target.value)}
              spellCheck={false}
              aria-label="Settings as JSON"
            />
            {settingsProblem(settings) && <p className="error">{settingsProblem(settings)}</p>}
          </Part>
        </div>
      </section>
    </div>
  )
}

function BoardRow({
  board,
  first,
  last,
  onChange,
  onMove,
  onRemove,
}: {
  board: Board
  first: boolean
  last: boolean
  onChange: (patch: Partial<Board>) => void
  onMove: (by: -1 | 1) => void
  onRemove: () => void
}) {
  const test = useTestBoard()
  const [tested, setTested] = useState<{ name: string; result: FeedTest }>()
  const kind = BOARD_KINDS.find((k) => k.value === board.kind)!
  const name = boardName(board)
  const result = tested?.name === name ? tested.result : undefined
  const needsWords = board.kind !== 'japan-dev' && board.kind !== 'tokyodev'
  return (
    <div className="paper-source">
      <div className="jobs-board-row">
        <label className="field">
          <span>Place</span>
          <select className="input" value={board.kind} onChange={(e) => onChange({ kind: e.target.value as Board['kind'], value: '' })}>
            {BOARD_KINDS.map((k) => (
              <option key={k.value} value={k.value}>
                {k.label}
              </option>
            ))}
          </select>
        </label>
        {needsWords ? (
          <label className="field">
            <span>{board.kind === 'company' ? 'Service and company' : 'Words to search'}</span>
            <input className="input mono" value={board.value} placeholder={kind.placeholder} onChange={(e) => onChange({ value: e.target.value })} />
          </label>
        ) : (
          <span />
        )}
        <button
          type="button"
          className="btn"
          disabled={(needsWords && !board.value.trim()) || test.isPending}
          onClick={() => test.mutate(name, { onSuccess: (r) => setTested({ name, result: r }) })}
        >
          {test.isPending ? <LoaderCircle size={14} className="spin" /> : null} Test
        </button>
        <span className="paper-section-move">
          <button type="button" className="btn btn-quiet" aria-label="Move up" disabled={first} onClick={() => onMove(-1)}>
            <ArrowUp size={14} />
          </button>
          <button type="button" className="btn btn-quiet" aria-label="Move down" disabled={last} onClick={() => onMove(1)}>
            <ArrowDown size={14} />
          </button>
          <button type="button" className="btn btn-quiet" aria-label={`Remove ${name || 'this place'}`} onClick={onRemove}>
            <Trash2 size={14} />
          </button>
        </span>
      </div>
      <span className="hint">{kind.hint}</span>
      {board.kind === 'company' && (
        <label className="check">
          <input type="checkbox" checked={board.allInJapan} onChange={(e) => onChange({ allInJapan: e.target.checked })} />
          Every job here is in Japan, whatever its place says (&quot;Hybrid&quot;)
        </label>
      )}
      {result && <TestResult {...result} what="offers" />}
      {test.error && <p className="error">{test.error.message}</p>}
    </div>
  )
}

function JudgeModel({ jobs, onChange }: { jobs: Jobs; onChange: (next: (j: Jobs) => Jobs) => void }) {
  const models = useRoutineModels()
  const list = models.data ?? []
  return (
    <Field label="Model that judges" hint={models.data && list.length === 0 ? 'No local models set up yet: offers wait for one.' : 'A model on this computer: nothing leaves it.'}>
      <select className="input" value={jobs.model} onChange={(e) => onChange((j) => ({ ...j, model: e.target.value }))}>
        {list.map((m) => (
          <option key={m.id} value={m.id}>
            {m.name}
          </option>
        ))}
        {!list.some((m) => m.id === jobs.model) && (
          <option value={jobs.model}>
            {jobs.model}
            {models.data ? ' (not set up)' : ''}
          </option>
        )}
      </select>
    </Field>
  )
}

function Channel({ jobs, onChange }: { jobs: Jobs; onChange: (next: (j: Jobs) => Jobs) => void }) {
  const channels = useRoutineChannels()
  const known = channels.data ?? []
  const channel = jobs.channel.trim().toUpperCase()
  return (
    <Field
      label="Telegram"
      hint={
        channels.data && known.length === 0
          ? 'No bots set up yet: offers wait in Openspace.'
          : 'The bot that sends each offer with its yes and no; Openspace listens to it, so it must be this routine’s alone.'
      }
    >
      <select className="input" value={channel} onChange={(e) => onChange((j) => ({ ...j, channel: e.target.value }))}>
        <option value="">Nowhere: offers wait in Openspace</option>
        {known.map((c) => (
          <option key={c} value={c}>
            {c}
          </option>
        ))}
        {channel && !known.includes(channel) && <option value={channel}>{channel} (not found)</option>}
      </select>
    </Field>
  )
}
