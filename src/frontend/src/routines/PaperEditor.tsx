import { useEffect, useRef, useState, type ReactNode } from 'react'
import { ArrowDown, ArrowUp, CircleCheck, CircleX, LoaderCircle, Plus, Trash2 } from 'lucide-react'
import { useCreateRoutine, usePaperPreview, useRoutineChannels, useRoutineModels, useTestFeed, useUpdateRoutine, type FeedTest } from '../api/hooks'
import type { RoutineView, Weekday } from '../api/types'
import { useToast } from '../ui/toastContext'
import { GRADES, gradeLabel, readKanji, withKanji } from './kanji'
import {
  FORMS,
  LENGTHS,
  PICTURE_SIZES,
  WIDTHS,
  hostOf,
  linesOf,
  mostColumns,
  move,
  newSection,
  paperProblems,
  readPaper,
  removeSection,
  renameSection,
  tidyPaper,
  wordsOf,
  writePaper,
  type Feed,
  type Paper,
  type PaperSection,
} from './paper'
import { describeSchedule, scheduleFrom, settingsProblem, toggleDay, WEEK } from './schedule'
import './paperEditor.css'

const PARTS = [
  { id: 'paper', label: 'The paper' },
  { id: 'reader', label: 'Who reads it' },
  { id: 'layout', label: 'Layout' },
  { id: 'sections', label: 'Sections' },
  { id: 'kanji', label: 'Kanji of the day' },
  { id: 'delivery', label: 'Delivery' },
  { id: 'when', label: 'Name and time' },
  { id: 'advanced', label: 'Advanced (JSON)' },
] as const

const LANGUAGES = ['English', 'Italian', 'Japanese', 'Spanish', 'French', 'German', 'Portuguese']

/**
 * The morning paper's settings on a page of their own: every setting a field with its
 * explanation under it, the sections with their sources and layout, and beside them the paper
 * laid out with sample text. The JSON stays at the end, for what has no field yet.
 */
const LOOK_BACK = [
  { hours: 12, label: '12 hours' },
  { hours: 24, label: '1 day' },
  { hours: 48, label: '2 days' },
  { hours: 72, label: '3 days' },
  { hours: 168, label: 'A week' },
]

export function PaperEditor({ officeId, routine, defaults, onDone }: { officeId: string; routine?: RoutineView; defaults: string; onDone: () => void }) {
  const create = useCreateRoutine(officeId)
  const update = useUpdateRoutine()
  const toast = useToast()
  const [settings, setSettings] = useState(routine?.settings ?? defaults)
  const [name, setName] = useState(routine?.name ?? 'Morning paper')
  const [days, setDays] = useState<Weekday[]>(routine?.schedule.days ?? [])
  const [at, setAt] = useState(routine?.schedule.at ?? '06:30')
  const [everyWeeks, setEveryWeeks] = useState(routine?.schedule.everyWeeks ?? 1)
  const [picked, setPicked] = useState(0)
  const [part, setPart] = useState<string>('paper')
  const body = useRef<HTMLDivElement>(null)

  const paper = readPaper(settings)
  const problems = paper ? paperProblems(paper) : [settingsProblem(settings) ?? 'The advanced settings are not valid JSON.']
  const change = (next: (p: Paper) => Paper) => paper && setSettings(writePaper(next(paper)))
  const error = create.error ?? update.error
  const preview = useDebounced(settings, 500)

  const hasFields = paper !== undefined
  // The index follows the part in view: the last one whose top has reached the upper third.
  useEffect(() => {
    const root = body.current
    const scroller = root?.closest('.main')
    if (!root || !scroller) return
    const follow = () => {
      const line = scroller.getBoundingClientRect().top + scroller.clientHeight / 3
      const parts = [...root.querySelectorAll<HTMLElement>('[id^="paper-part-"]')]
      const current = parts.filter((el) => el.getBoundingClientRect().top <= line).pop() ?? parts[0]
      if (current) setPart(current.id.replace('paper-part-', ''))
    }
    scroller.addEventListener('scroll', follow, { passive: true })
    return () => scroller.removeEventListener('scroll', follow)
  }, [hasFields])

  const save = () => {
    if (!paper || problems.length > 0) return
    const tidy = writePaper(tidyPaper(paper))
    const schedule = scheduleFrom(days, at, everyWeeks)
    if (routine) {
      update.mutate(
        { id: routine.id, name, schedule, settings: tidy },
        {
          onSuccess: () => {
            toast('Paper saved')
            onDone()
          },
        },
      )
    } else {
      create.mutate(
        { name, kind: 'morning-paper', schedule, settings: tidy },
        {
          onSuccess: () => {
            toast('Paper created')
            onDone()
          },
        },
      )
    }
  }

  const go = (id: string) => {
    setPart(id)
    document.getElementById(`paper-part-${id}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  return (
    <div className="paper-editor-room">
      <section className="paper-editor" aria-label={routine ? `Change ${routine.name}` : 'New morning paper'}>
        <header className="paper-editor-head">
          <div>
            <p className="hint">Routines › {routine ? 'Change' : 'New'}</p>
            <h1>{paper?.title || name}</h1>
          </div>
          <div className="paper-editor-actions">
            <button type="button" className="btn btn-quiet" onClick={onDone}>
              Cancel
            </button>
            <button type="button" className="btn btn-primary" onClick={save} disabled={problems.length > 0 || create.isPending || update.isPending}>
              {routine ? 'Save paper' : 'Create paper'}
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

        <nav className="paper-index" aria-label="Parts of the paper's settings">
          {PARTS.map((p) => (
            <button key={p.id} type="button" aria-current={part === p.id ? 'true' : undefined} onClick={() => go(p.id)}>
              {p.label}
            </button>
          ))}
        </nav>

        <div className="paper-form" ref={body}>
          {paper ? (
            <>
              <Part id="paper" title="The paper">
                <div className="paper-grid">
                  <Field label="Title" hint="Printed large at the top of the front page.">
                    <input className="input" value={paper.title} onChange={(e) => change((p) => ({ ...p, title: e.target.value }))} />
                  </Field>
                  <Field label="Subtitle" hint="A line under the title; empty for none.">
                    <input className="input" value={paper.subtitle} onChange={(e) => change((p) => ({ ...p, subtitle: e.target.value }))} />
                  </Field>
                  <Field label="Language" hint="The paper is written in it, whatever the language of its sources.">
                    <select className="input" value={paper.language} onChange={(e) => change((p) => ({ ...p, language: e.target.value }))}>
                      {[...new Set([...LANGUAGES, paper.language])].map((l) => (
                        <option key={l}>{l}</option>
                      ))}
                    </select>
                  </Field>
                  <Field label="Folder" hint="Where every issue is kept, as PDF and as a web page.">
                    <input
                      className="input mono"
                      value={paper.folder}
                      onChange={(e) => change((p) => ({ ...p, folder: e.target.value }))}
                      placeholder="~/Documents/Morning paper"
                    />
                  </Field>
                </div>
              </Part>

              <Part id="reader" title="Who reads it">
                <Field label="The reader" hint="Who reads it and what they care about, in a few sentences: the model chooses and writes for them.">
                  <textarea className="input" rows={3} value={paper.reader} onChange={(e) => change((p) => ({ ...p, reader: e.target.value }))} />
                </Field>
                <Lines
                  label="How to write"
                  hint="One rule per line, for every article."
                  value={paper.rules}
                  onChange={(rules) => change((p) => ({ ...p, rules }))}
                />
                <div className="paper-grid">
                  <Lines
                    label="Never write"
                    hint="Phrases the writer must never use, one per line: its habits, such as a set greeting or a closing it repeats."
                    value={paper.neverWrite}
                    onChange={(neverWrite) => change((p) => ({ ...p, neverWrite }))}
                  />
                  <div className="field">
                    <label className="check">
                      <input type="checkbox" checked={paper.proofread} onChange={(e) => change((p) => ({ ...p, proofread: e.target.checked }))} />
                      Proofread before printing
                    </label>
                    <span className="hint">
                      One last read of every article for mistakes of grammar and spelling, facts and style left alone. It takes a few minutes more.
                    </span>
                  </div>
                </div>
                <div className="paper-grid">
                  <Lines
                    label="What matters most"
                    hint="One topic per line: after importance, they decide the front page, and they count in every section."
                    value={paper.priorities}
                    onChange={(priorities) => change((p) => ({ ...p, priorities }))}
                  />
                  <Lines
                    label="Never print"
                    hint="One topic per line: left out everywhere, good news included."
                    value={paper.avoid}
                    onChange={(avoid) => change((p) => ({ ...p, avoid }))}
                  />
                </div>
                <div className="paper-grid">
                  <Field label="News from the last" hint="Hours; older news is left out.">
                    <input
                      className="input"
                      type="number"
                      min={1}
                      max={168}
                      value={paper.hoursBack}
                      onChange={(e) =>
                        change((p) => ({
                          ...p,
                          hoursBack: clamp(e.target.value, 1, 168, 24),
                        }))
                      }
                    />
                  </Field>
                  <Field label="Articles per section" hint="At most; each section can change it below.">
                    <input
                      className="input"
                      type="number"
                      min={1}
                      max={10}
                      value={paper.perSection}
                      onChange={(e) =>
                        change((p) => ({
                          ...p,
                          perSection: clamp(e.target.value, 1, 10, 4),
                        }))
                      }
                    />
                  </Field>
                </div>
              </Part>

              <Part id="layout" title="Layout">
                <p className="hint">For the whole paper. Each section can change its own below; the preview shows the result.</p>
                <Choice
                  label="Text columns"
                  hint="Of a full-width section. Half-width sections have two at most, a third one."
                  value={paper.layout.columns}
                  options={[1, 2, 3, 4].map((n) => ({
                    value: n,
                    label: String(n),
                  }))}
                  onChange={(columns) => change((p) => ({ ...p, layout: { ...p.layout, columns } }))}
                />
                <Choice
                  label="Front page picture"
                  hint="The opening story's photo, when its source has one."
                  value={paper.layout.frontPicture}
                  options={PICTURE_SIZES}
                  onChange={(frontPicture) =>
                    change((p) => ({
                      ...p,
                      layout: { ...p.layout, frontPicture },
                    }))
                  }
                />
                <Choice
                  label="Section pictures"
                  hint="The photo of each section's first story."
                  value={paper.layout.pictures}
                  options={PICTURE_SIZES}
                  onChange={(pictures) => change((p) => ({ ...p, layout: { ...p.layout, pictures } }))}
                />
                <label className="check">
                  <input
                    type="checkbox"
                    checked={paper.layout.adapt}
                    onChange={(e) => change((p) => ({ ...p, layout: { ...p.layout, adapt: e.target.checked } }))}
                  />
                  The space follows the news
                </label>
                <span className="hint">
                  {paper.layout.adapt
                    ? "Each day the section with the biggest story spans the page with its picture, and quiet sections are told in brief, side by side at the end. The preview shows the sections as they are set."
                    : 'Off: every section keeps its width and form every day.'}
                </span>
              </Part>

              <Part id="sections" title="Sections">
                <p className="hint">In the order they are printed. Sections of half or a third of the width sit side by side.</p>
                <div className="paper-sections">
                  <ul className="paper-section-list" aria-label="Sections">
                    {paper.sections.map((s, i) => (
                      <li key={i} className={i === picked ? 'picked' : undefined}>
                        <button type="button" className="paper-section-pick" aria-pressed={i === picked} onClick={() => setPicked(i)}>
                          <span>{s.name || `Section ${i + 1}`}</span>
                          <span className="hint">
                            {WIDTHS.find((w) => w.value === s.width)?.label} · {sourcesLabel(s.feeds.filter((f) => f.url.trim()).length)}
                          </span>
                        </button>
                        <span className="paper-section-move">
                          <button
                            type="button"
                            className="btn btn-quiet"
                            aria-label={`Move ${s.name} up`}
                            disabled={i === 0}
                            onClick={() => {
                              change((p) => ({
                                ...p,
                                sections: move(p.sections, i, -1),
                              }))
                              setPicked(i - 1)
                            }}
                          >
                            <ArrowUp size={14} />
                          </button>
                          <button
                            type="button"
                            className="btn btn-quiet"
                            aria-label={`Move ${s.name} down`}
                            disabled={i === paper.sections.length - 1}
                            onClick={() => {
                              change((p) => ({
                                ...p,
                                sections: move(p.sections, i, 1),
                              }))
                              setPicked(i + 1)
                            }}
                          >
                            <ArrowDown size={14} />
                          </button>
                        </span>
                      </li>
                    ))}
                    <li>
                      <button
                        type="button"
                        className="btn"
                        onClick={() => {
                          change((p) => ({
                            ...p,
                            sections: [...p.sections, newSection(p.sections.map((s) => s.name))],
                          }))
                          setPicked(paper.sections.length)
                        }}
                      >
                        <Plus size={14} /> Add a section
                      </button>
                    </li>
                  </ul>
                  {paper.sections[picked] ? (
                    <SectionForm
                      key={picked}
                      paper={paper}
                      index={picked}
                      onChange={change}
                      onRemove={() => {
                        change((p) => removeSection(p, picked))
                        setPicked(Math.max(0, picked - 1))
                      }}
                    />
                  ) : (
                    <p className="hint">No sections yet: Add a section makes the first one.</p>
                  )}
                </div>
              </Part>

              <Part id="kanji" title="Kanji of the day">
                <KanjiPart settings={settings} onChange={setSettings} />
              </Part>

              <Part id="delivery" title="Delivery">
                <Delivery paper={paper} onChange={change} />
              </Part>
            </>
          ) : (
            <p className="error">The advanced settings are not valid JSON: fix them at the end of the page to see the fields again.</p>
          )}

          <Part id="when" title="Name and time">
            <div className="paper-grid">
              <Field label="Routine name" hint="How the routine is called in Openspace; the paper's own title is above.">
                <input className="input" value={name} onChange={(e) => setName(e.target.value)} maxLength={80} required />
              </Field>
              <Field label="At" hint="On this computer's clock.">
                <input className="input" type="time" value={at} onChange={(e) => setAt(e.target.value)} required />
              </Field>
            </div>
            <fieldset className="field">
              <legend className="paper-legend">Days</legend>
              <div className="choice">
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
              <span className="hint">
                {describeSchedule(scheduleFrom(days, at, everyWeeks))}
                {days.length === 0 && '. Pick days to print only on those.'}
              </span>
            </fieldset>
            {days.length > 0 && (
              <Choice
                label="Every"
                value={everyWeeks}
                options={[1, 2, 3, 4].map((n) => ({
                  value: n,
                  label: n === 1 ? 'Week' : `${n} weeks`,
                }))}
                onChange={setEveryWeeks}
              />
            )}
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

        <Preview settings={preview} ready={Boolean(paper) && (paper?.sections.length ?? 0) > 0} />
      </section>
    </div>
  )
}

function SectionForm({
  paper,
  index,
  onChange,
  onRemove,
}: {
  paper: Paper
  index: number
  onChange: (next: (p: Paper) => Paper) => void
  onRemove: () => void
}) {
  const s = paper.sections[index]
  const set = (patch: Partial<PaperSection>) =>
    onChange((p) => ({
      ...p,
      sections: p.sections.map((x, i) => (i === index ? { ...x, ...patch } : x)),
    }))
  const most = mostColumns(s.width)
  const goodFromAll = paper.goodNews.length === 0
  const good = goodFromAll || paper.goodNews.includes(s.name)
  const setGood = (on: boolean) =>
    onChange((p) => {
      const names = p.sections.map((x) => x.name)
      const chosen = p.goodNews.length === 0 ? names : p.goodNews
      const next = on ? [...new Set([...chosen, s.name])] : chosen.filter((n) => n !== s.name)
      return { ...p, goodNews: next.length === names.length ? [] : next }
    })
  const paperSize = PICTURE_SIZES.find((x) => x.value === paper.layout.pictures)?.label ?? ''

  return (
    <div className="paper-section-form">
      <div className="paper-grid">
        <Field label="Name" hint="Printed as the section's heading.">
          <input className="input" value={s.name} onChange={(e) => onChange((p) => renameSection(p, index, e.target.value))} />
        </Field>
        <Field label="What it covers" hint="For the model, in a few words.">
          <input className="input" value={s.about} onChange={(e) => set({ about: e.target.value })} />
        </Field>
      </div>

      <h3 className="paper-subhead">Sources</h3>
      <Sources feeds={s.feeds} onChange={(feeds) => set({ feeds })} />

      <h3 className="paper-subhead">On the page</h3>
      <Choice label="Form" hint={FORMS.find((f) => f.value === s.form)?.hint} value={s.form} options={FORMS} onChange={(form) => set({ form })} />
      <Choice
        label="Width"
        hint="Half and third sections sit side by side, in their order."
        value={s.width}
        options={WIDTHS}
        onChange={(width) =>
          set({
            width,
            columns: s.columns !== null ? Math.min(s.columns, mostColumns(width)) : null,
          })
        }
      />
      <Choice
        label="Text columns"
        value={s.columns ?? 0}
        options={[
          {
            value: 0,
            label: `Paper's (${Math.min(paper.layout.columns, most)})`,
          },
          ...[1, 2, 3, 4].filter((n) => n <= most).map((n) => ({ value: n, label: String(n) })),
        ]}
        onChange={(n) => set({ columns: n === 0 ? null : n })}
      />
      <Choice
        label="Picture"
        hint="The photo of the first story, when its source has one."
        value={s.picture ?? 'paper'}
        options={[{ value: 'paper', label: `Paper's (${paperSize})` }, ...PICTURE_SIZES]}
        onChange={(v) => set({ picture: v === 'paper' ? null : v })}
      />
      <Choice
        label="Picture place"
        hint={s.width === 'full' ? undefined : 'Narrow sections always put it above the story.'}
        value={s.width === 'full' ? s.pictureAt : 'above'}
        disabled={s.width !== 'full'}
        options={[
          { value: 'beside', label: 'Beside the first story' },
          { value: 'above', label: 'Above it' },
        ]}
        onChange={(pictureAt) => set({ pictureAt })}
      />
      <div className="paper-grid">
        <Field label="Articles" hint="At most: fewer when there is less news.">
          <select
            className="input"
            value={s.articles ?? 0}
            onChange={(e) =>
              set({
                articles: Number(e.target.value) === 0 ? null : Number(e.target.value),
              })
            }
          >
            <option value={0}>Paper's ({paper.perSection})</option>
            {Array.from({ length: 10 }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </Field>
        <Choice
          label="Length"
          hint={s.form === 'articles' ? LENGTHS.find((l) => l.value === s.length)?.hint : 'Set by the form: briefs are one sentence, the main story is long.'}
          value={s.length}
          options={LENGTHS}
          disabled={s.form !== 'articles'}
          onChange={(length) => set({ length })}
        />
      </div>

      <h3 className="paper-subhead">What goes in</h3>
      <div className="paper-grid">
        <Field label="News from the last" hint="For outlets that write every few days, look further back than the rest of the paper.">
          <select
            className="input"
            value={s.hoursBack ?? 0}
            onChange={(e) =>
              set({
                hoursBack: Number(e.target.value) === 0 ? null : Number(e.target.value),
              })
            }
          >
            <option value={0}>Paper's ({paper.hoursBack} hours)</option>
            {s.hoursBack !== null && !LOOK_BACK.some((l) => l.hours === s.hoursBack) && <option value={s.hoursBack}>{s.hoursBack} hours</option>}
            {LOOK_BACK.map((l) => (
              <option key={l.hours} value={l.hours}>
                {l.label}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Only if" hint='A condition every article must meet, such as "it happened in Italy"; empty for none.'>
          <input className="input" value={s.only} onChange={(e) => set({ only: e.target.value })} />
        </Field>
        <Field
          label={s.onlyWithKeywords ? 'Names and words it needs' : 'Words to look for'}
          hint={
            s.onlyWithKeywords
              ? 'Separated by commas: only news naming one of them is printed. Write names as the outlets do, in every alphabet they use.'
              : 'Separated by commas: news with them comes first. Starts of words work too.'
          }
        >
          <input className="input" value={s.keywords.join(', ')} onChange={(e) => set({ keywords: wordsOf(e.target.value) })} />
        </Field>
      </div>
      <label className="check">
        <input type="checkbox" checked={s.onlyWithKeywords} onChange={(e) => set({ onlyWithKeywords: e.target.checked })} />
        Print it only on days with news about these words
      </label>
      <span className="hint">
        {s.onlyWithKeywords
          ? 'A section for the artists, games or people you follow: on a day without news about them, it is left out.'
          : 'Off: the section prints every day, with the words only putting their news first.'}
      </span>
      <Lines
        label="Topics to look out for"
        hint="One per line, on top of what matters most to the reader."
        value={s.focus}
        onChange={(focus) => set({ focus })}
      />
      <Field label="Style" hint="How to write this section, on top of the paper's rules; empty for none.">
        <textarea className="input" rows={2} value={s.style} onChange={(e) => set({ style: e.target.value })} />
      </Field>
      <label className="check">
        <input type="checkbox" checked={good} onChange={(e) => setGood(e.target.checked)} />
        The good news of the day may come from here
      </label>
      <span className="hint">
        {goodFromAll ? 'Every section may give it, as long as none is left out.' : `It comes from: ${paper.goodNews.join(', ') || 'no section'}.`}
      </span>

      <div className="paper-section-remove">
        <button type="button" className="btn btn-danger" onClick={onRemove}>
          <Trash2 size={14} /> Remove {s.name || 'this section'}
        </button>
      </div>
    </div>
  )
}

function Sources({ feeds, onChange }: { feeds: Feed[]; onChange: (feeds: Feed[]) => void }) {
  const set = (i: number, patch: Partial<Feed>) => onChange(feeds.map((f, j) => (j === i ? { ...f, ...patch } : f)))
  return (
    <div className="paper-sources">
      {feeds.length === 0 && <p className="hint">No sources yet: the section prints only with at least one.</p>}
      {feeds.map((f, i) => (
        <SourceRow key={i} feed={f} onChange={(patch) => set(i, patch)} onRemove={() => onChange(feeds.filter((_, j) => j !== i))} />
      ))}
      <div>
        <button type="button" className="btn" onClick={() => onChange([...feeds, { name: '', url: '' }])}>
          <Plus size={14} /> Add a source
        </button>
      </div>
    </div>
  )
}

function SourceRow({ feed, onChange, onRemove }: { feed: Feed; onChange: (patch: Partial<Feed>) => void; onRemove: () => void }) {
  const test = useTestFeed()
  const [tested, setTested] = useState<{ url: string; result: FeedTest }>()
  const result = tested?.url === feed.url ? tested.result : undefined
  return (
    <div className="paper-source">
      <div className="paper-source-row">
        <label className="field">
          <span>Name</span>
          <input
            className="input"
            value={feed.name}
            placeholder={feed.url ? hostOf(feed.url) : 'e.g. ANSA'}
            onChange={(e) => onChange({ name: e.target.value })}
          />
        </label>
        <label className="field">
          <span>Feed address (RSS or Atom)</span>
          <input className="input mono" value={feed.url} placeholder="https://…/feed" onChange={(e) => onChange({ url: e.target.value })} />
        </label>
        <button
          type="button"
          className="btn"
          disabled={!feed.url.trim() || test.isPending}
          onClick={() =>
            test.mutate(feed.url, {
              onSuccess: (r) => setTested({ url: feed.url, result: r }),
            })
          }
        >
          {test.isPending ? <LoaderCircle size={14} className="spin" /> : null} Test
        </button>
        <button type="button" className="btn btn-quiet" aria-label={`Remove ${feed.name || 'this source'}`} onClick={onRemove}>
          <Trash2 size={14} />
        </button>
      </div>
      {result &&
        (result.ok ? (
          <div className="paper-source-result ok">
            <p>
              <CircleCheck size={14} aria-hidden /> It works: {result.count} news. The latest:
            </p>
            <ul>
              {result.titles.map((t) => (
                <li key={t}>{t}</li>
              ))}
            </ul>
          </div>
        ) : (
          <p className="paper-source-result error">
            <CircleX size={14} aria-hidden /> {result.problem}
          </p>
        ))}
      {test.error && <p className="error">{test.error.message}</p>}
    </div>
  )
}

function KanjiPart({ settings, onChange }: { settings: string; onChange: (settings: string) => void }) {
  const choice = readKanji(settings)
  return (
    <>
      <label className="check">
        <input type="checkbox" checked={choice.enabled} onChange={(e) => onChange(withKanji(settings, { ...choice, enabled: e.target.checked }))} />
        Close every issue with one kanji
      </label>
      <span className="hint">Its readings, how to draw it stroke by stroke, words and sentences with their reading. Never the same one twice.</span>
      {choice.enabled && (
        <Field label="Start from" hint="The school year the kanji are taught in Japan.">
          <select
            className="input"
            value={choice.fromGrade}
            onChange={(e) =>
              onChange(
                withKanji(settings, {
                  ...choice,
                  fromGrade: Number(e.target.value),
                }),
              )
            }
          >
            {GRADES.map((g) => (
              <option key={g} value={g}>
                {gradeLabel(g)}
              </option>
            ))}
          </select>
        </Field>
      )}
    </>
  )
}

function Delivery({ paper, onChange }: { paper: Paper; onChange: (next: (p: Paper) => Paper) => void }) {
  const channels = useRoutineChannels()
  const models = useRoutineModels()
  const channel = paper.channel.trim().toUpperCase()
  const known = channels.data ?? []
  const modelList = models.data ?? []
  return (
    <div className="paper-grid">
      <Field
        label="Telegram"
        hint={
          channels.data && known.length === 0
            ? 'No bots set up yet: the paper is only kept in its folder.'
            : 'The bot that sends the PDF every morning, in its topic of your group.'
        }
      >
        <select className="input" value={channel} onChange={(e) => onChange((p) => ({ ...p, channel: e.target.value }))}>
          <option value="">Nowhere: only keep it in the folder</option>
          {known.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
          {channel && !known.includes(channel) && <option value={channel}>{channel} (not found)</option>}
        </select>
      </Field>
      <Field
        label="Model that writes it"
        hint={models.data && modelList.length === 0 ? 'No local models set up yet: the paper waits for one.' : 'A model on this computer: nothing leaves it.'}
      >
        <select className="input" value={paper.model} onChange={(e) => onChange((p) => ({ ...p, model: e.target.value }))}>
          {modelList.map((m) => (
            <option key={m.id} value={m.id}>
              {m.name}
            </option>
          ))}
          {!modelList.some((m) => m.id === paper.model) && (
            <option value={paper.model}>
              {paper.model}
              {models.data ? ' (not set up)' : ''}
            </option>
          )}
        </select>
      </Field>
    </div>
  )
}

/** The paper laid out with sample text, scaled to the column, updated as the settings change. */
function Preview({ settings, ready }: { settings: string; ready: boolean }) {
  const page = usePaperPreview(settings, ready)
  const frame = useRef<HTMLIFrameElement>(null)
  const box = useRef<HTMLDivElement>(null)
  const [scale, setScale] = useState(0.5)
  const [height, setHeight] = useState(1123)
  const PAGE = 794 // A4 at 96 dpi

  useEffect(() => {
    const el = box.current
    if (!el) return
    const watch = new ResizeObserver(() => setScale(Math.min(1, el.clientWidth / PAGE)))
    watch.observe(el)
    return () => watch.disconnect()
  }, [])

  return (
    <aside className="paper-preview" aria-label="Preview">
      <div className="paper-preview-head">
        <h2>Preview</h2>
        {page.isFetching && <LoaderCircle size={14} className="spin" aria-label="Updating" />}
      </div>
      <p className="hint">Sample text in your layout: the real one comes from the news of the day. Pages break when printed.</p>
      {!ready && <p className="hint">Add a section to see the paper.</p>}
      {page.error && <p className="error">{page.error.message}</p>}
      <div className="paper-preview-box" ref={box} style={{ height: height * scale }}>
        {page.data && ready && (
          <iframe
            ref={frame}
            title="The paper with sample text"
            sandbox="allow-same-origin"
            srcDoc={page.data}
            style={{ width: PAGE, height, transform: `scale(${scale})` }}
            onLoad={() => setHeight(frame.current?.contentDocument?.documentElement.scrollHeight ?? 1123)}
          />
        )}
      </div>
    </aside>
  )
}

function Part({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section id={`paper-part-${id}`} className="paper-part" aria-labelledby={`paper-part-${id}-title`}>
      <h2 id={`paper-part-${id}-title`}>{title}</h2>
      {children}
    </section>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {hint && <span className="hint">{hint}</span>}
    </label>
  )
}

/** A list typed one item per line. Kept as typed while editing, so blank lines can be added. */
function Lines({ label, hint, value, onChange }: { label: string; hint: string; value: string[]; onChange: (lines: string[]) => void }) {
  const [draft, setDraft] = useState(value.join('\n'))
  useEffect(() => {
    if (linesOf(draft).join('\n') !== value.join('\n')) setDraft(value.join('\n'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value.join('\n')])
  return (
    <Field label={label} hint={hint}>
      <textarea
        className="input"
        rows={Math.min(8, Math.max(3, value.length + 1))}
        value={draft}
        onChange={(e) => {
          setDraft(e.target.value)
          onChange(linesOf(e.target.value))
        }}
      />
    </Field>
  )
}

function Choice<T extends string | number>({
  label,
  hint,
  value,
  options,
  onChange,
  disabled,
}: {
  label: string
  hint?: string
  value: T
  options: readonly { value: T; label: string }[]
  onChange: (value: T) => void
  disabled?: boolean
}) {
  return (
    <fieldset className="field paper-choice" disabled={disabled}>
      <legend className="paper-legend">{label}</legend>
      <div className="choice">
        {options.map((o) => (
          <button key={String(o.value)} type="button" className="btn" aria-pressed={o.value === value} onClick={() => onChange(o.value)}>
            {o.value === value && <CircleCheck size={14} aria-hidden />} {o.label}
          </button>
        ))}
      </div>
      {hint && <span className="hint">{hint}</span>}
    </fieldset>
  )
}

const sourcesLabel = (n: number): string => (n === 1 ? '1 source' : `${n} sources`)

function clamp(text: string, min: number, max: number, fallback: number): number {
  const n = Number(text)
  return Number.isInteger(n) ? Math.min(max, Math.max(min, n)) : fallback
}

function useDebounced<T>(value: T, ms: number): T {
  const [settled, setSettled] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), ms)
    return () => clearTimeout(timer)
  }, [value, ms])
  return settled
}
