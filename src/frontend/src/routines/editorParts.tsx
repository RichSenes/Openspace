import { useEffect, useState, type ReactNode } from 'react'
import { CircleCheck, CircleX } from 'lucide-react'
import type { Weekday } from '../api/types'
import { linesOf } from './paper'
import { describeSchedule, scheduleFrom, toggleDay, WEEK } from './schedule'

/** What the routine editors share: parts with an index, fields with their hint, choices, the schedule. */

export function Part({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section id={`paper-part-${id}`} className="paper-part" aria-labelledby={`paper-part-${id}-title`}>
      <h2 id={`paper-part-${id}-title`}>{title}</h2>
      {children}
    </section>
  )
}

export function Field({ label, hint, children }: { label: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {hint && <span className="hint">{hint}</span>}
    </label>
  )
}

/** A list typed one item per line. Kept as typed while editing, so blank lines can be added. */
export function Lines({ label, hint, value, onChange }: { label: string; hint: string; value: string[]; onChange: (lines: string[]) => void }) {
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

export function Choice<T extends string | number>({
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

/** A test's outcome under what was tested: what came back, or why nothing did. */
export function TestResult({ ok, count, titles, problem, what }: { ok: boolean; count: number; titles: string[]; problem?: string | null; what: string }) {
  return ok ? (
    <div className="paper-source-result ok">
      <p>
        <CircleCheck size={14} aria-hidden /> It works: {count} {what}. The latest:
      </p>
      <ul>
        {titles.map((t) => (
          <li key={t}>{t}</li>
        ))}
      </ul>
    </div>
  ) : (
    <p className="paper-source-result error">
      <CircleX size={14} aria-hidden /> {problem}
    </p>
  )
}

/** The routine's name, the time and the days it runs. */
export function ScheduleFields(props: {
  name: string
  onName: (name: string) => void
  nameHint: string
  at: string
  onAt: (at: string) => void
  days: Weekday[]
  onDays: (days: Weekday[]) => void
  everyWeeks: number
  onEveryWeeks: (weeks: number) => void
  doing: string
}) {
  const { name, onName, nameHint, at, onAt, days, onDays, everyWeeks, onEveryWeeks, doing } = props
  return (
    <>
      <div className="paper-grid">
        <Field label="Routine name" hint={nameHint}>
          <input className="input" value={name} onChange={(e) => onName(e.target.value)} maxLength={80} required />
        </Field>
        <Field label="At" hint="On this computer's clock.">
          <input className="input" type="time" value={at} onChange={(e) => onAt(e.target.value)} required />
        </Field>
      </div>
      <fieldset className="field">
        <legend className="paper-legend">Days</legend>
        <div className="choice">
          {WEEK.map((w) => (
            <button key={w.day} type="button" className="btn day-toggle" aria-pressed={days.includes(w.day)} onClick={() => onDays(toggleDay(days, w.day))}>
              {days.includes(w.day) && <CircleCheck size={14} aria-hidden />} {w.short}
            </button>
          ))}
        </div>
        <span className="hint">
          {describeSchedule(scheduleFrom(days, at, everyWeeks))}
          {days.length === 0 && `. Pick days to ${doing} only on those.`}
        </span>
      </fieldset>
      {days.length > 0 && (
        <Choice
          label="Every"
          value={everyWeeks}
          options={[1, 2, 3, 4].map((n) => ({ value: n, label: n === 1 ? 'Week' : `${n} weeks` }))}
          onChange={onEveryWeeks}
        />
      )}
    </>
  )
}
