import { useState } from 'react'
import type { OfficeView } from '../api/types'
import { useToast } from '../ui/toastContext'
import { formatMoment } from '../ui/dates'
import { useChangeDesk, useCharacters } from './api'
import {
  affinityWord,
  birthdayLabel,
  daysIn,
  moodPath,
  moodWord,
  signed,
  SHIFTS,
  tiesOf,
  TRAIT_LABELS,
  TRUST_LEVELS,
  TRUSTED_ACTIONS,
  trustOf,
  type CharacterBoardView,
  type CharacterView,
  type Shift,
  type TrustLevel,
} from './characters'
import './characters.css'

/**
 * Who each inhabitant is and how they are doing: traits, birthday, shift, trust, seniority,
 * mood with its week and the relationships with what made them. For the person only: agents
 * never see these numbers.
 */
export function CharactersPanel({ office }: { office: OfficeView }) {
  const board = useCharacters(office.id)

  return (
    <section className="characters">
      <header className="characters-head">
        <h1>Characters of {office.name}</h1>
        <p className="hint">
          Numbers come from what really happened in Openspace, never from dice. They change how the inhabitants talk, never the quality of their work, and the agents never see them.
        </p>
      </header>

      {board.isPending ? (
        <p className="hint">Reading the office…</p>
      ) : board.error ? (
        <p className="characters-error" role="alert">The characters could not be read: {board.error.message}</p>
      ) : board.data.characters.length === 0 ? (
        <div className="characters-empty">
          <h2>No inhabitants yet</h2>
          <p className="hint">Add a desk to a room: each desk is an inhabitant, born with its traits and a birthday.</p>
        </div>
      ) : (
        <>
          {board.data.experts.length > 0 && (
            <p className="characters-experts">
              <strong>Room experts:</strong> {board.data.experts.map((e) => `${e.deskName} in ${e.roomName} (${e.done} tasks)`).join(' · ')}
            </p>
          )}
          <div className="character-grid">
            {board.data.characters.map((c) => (
              <CharacterCard key={c.deskId} officeId={office.id} character={c} board={board.data} now={board.dataUpdatedAt} />
            ))}
          </div>
        </>
      )}
    </section>
  )
}

function CharacterCard({ officeId, character: c, board, now }: { officeId: string; character: CharacterView; board: CharacterBoardView; now: number }) {
  const change = useChangeDesk(officeId)
  const toast = useToast()
  const [editing, setEditing] = useState(false)
  const [month, setMonth] = useState(c.birthday.month)
  const [day, setDay] = useState(c.birthday.day)
  const ties = tiesOf(board, c.deskId)
  const done = (message: string) => ({ onSuccess: () => toast(message) })
  const titleId = `character-${c.deskId}`

  return (
    <article className="character" aria-labelledby={titleId}>
      <header className="character-head">
        <h2 id={titleId}>{c.name}</h2>
        <span className="hint">{c.roomName}</span>
      </header>

      <ul className="character-traits" aria-label="Traits">
        {c.traits.map((t) => (
          <li key={t}>{TRAIT_LABELS[t] ?? t}</li>
        ))}
      </ul>

      <dl className="character-facts">
        <dt>Birthday</dt>
        <dd>
          {editing ? (
            <form
              className="character-birthday"
              onSubmit={(e) => {
                e.preventDefault()
                change.mutate({ deskId: c.deskId, kind: 'birthday', month, day: Math.min(day, daysIn(month)) }, { onSuccess: () => { setEditing(false); toast('Birthday saved') } })
              }}
            >
              <select className="input" aria-label="Day" value={Math.min(day, daysIn(month))} onChange={(e) => setDay(Number(e.target.value))}>
                {Array.from({ length: daysIn(month) }, (_, i) => i + 1).map((d) => (
                  <option key={d} value={d}>{d}</option>
                ))}
              </select>
              <select className="input" aria-label="Month" value={month} onChange={(e) => setMonth(Number(e.target.value))}>
                {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                  <option key={m} value={m}>{birthdayLabel({ month: m, day: 1 }).split(' ')[1]}</option>
                ))}
              </select>
              <button className="btn btn-primary" type="submit" disabled={change.isPending}>Save birthday</button>
              <button className="btn btn-quiet" type="button" onClick={() => setEditing(false)}>Cancel</button>
            </form>
          ) : (
            <>
              {birthdayLabel(c.birthday)} <span className="hint">{c.birthday.chosen ? '(chosen by you)' : '(picked at birth)'}</span>{' '}
              <button className="btn btn-quiet" type="button" onClick={() => setEditing(true)}>Change birthday</button>
              {c.birthday.chosen && (
                <button className="btn btn-quiet" type="button" onClick={() => change.mutate({ deskId: c.deskId, kind: 'birthday-reset' }, done('Birthday given back'))}>
                  Use the one picked at birth
                </button>
              )}
            </>
          )}
        </dd>

        <dt>Shift</dt>
        <dd>
          <select
            className="input"
            aria-label={`Shift of ${c.name}`}
            value={c.shift}
            onChange={(e) => change.mutate({ deskId: c.deskId, kind: 'shift', shift: e.target.value as Shift }, done('Shift saved'))}
          >
            {SHIFTS.map((s) => (
              <option key={s.id} value={s.id}>{s.label}</option>
            ))}
          </select>
        </dd>

        <dt>Seniority</dt>
        <dd>
          {c.seniority.map((s) => (
            <span key={s.roomId}>
              {s.done} {s.done === 1 ? 'task' : 'tasks'} done in {s.roomName}, {s.merged} merged{s.expert && <strong className="character-expert"> · the room's expert</strong>}
            </span>
          ))}
        </dd>
      </dl>

      <fieldset className="character-trust">
        <legend>Trust</legend>
        {TRUSTED_ACTIONS.map((a) => (
          <label key={a.id} className="field">
            <span>{a.label}</span>
            <select
              className="input"
              value={trustOf(c, a.id)}
              onChange={(e) => change.mutate({ deskId: c.deskId, kind: 'trust', action: a.id, level: e.target.value as TrustLevel }, done('Trust saved'))}
            >
              {TRUST_LEVELS.map((l) => (
                <option key={l.id} value={l.id}>{l.label}</option>
              ))}
            </select>
            <small className="hint">{a.hint}</small>
          </label>
        ))}
      </fieldset>

      <section className="character-mood" aria-label="Mood">
        <h3>
          Mood: {c.mood.value} · {moodWord(c.mood.value)} <span className="hint">(rests at {c.mood.baseline})</span>
        </h3>
        <svg className="mood-week" viewBox="0 0 200 40" preserveAspectRatio="none" role="img" aria-label={`Mood over the last week, now ${c.mood.value}`}>
          <line x1="0" x2="200" y1={(1 - c.mood.baseline / 100) * 40} y2={(1 - c.mood.baseline / 100) * 40} className="mood-baseline" />
          <polyline points={moodPath(c.mood, now, 200, 40)} className="mood-line" />
        </svg>
        {c.mood.history.length === 0 ? (
          <p className="hint">Nothing moved it this week.</p>
        ) : (
          <ol className="character-history">
            {[...c.mood.history].reverse().map((p, i) => (
              <li key={`${p.at}-${i}`}>
                <span className={p.change > 0 ? 'up' : 'down'}>{signed(p.change)}</span> {p.reason} <span className="hint">{formatMoment(p.at)}</span>
              </li>
            ))}
          </ol>
        )}
      </section>

      <section className="character-ties" aria-label="Relationships">
        <h3>Relationships</h3>
        {ties.length === 0 ? (
          <p className="hint">No ties yet: they grow from reviews, shared files, failures made good and merge conflicts.</p>
        ) : (
          ties.map(({ other, tie }) => (
            <details key={`${tie.firstId}-${tie.secondId}`}>
              <summary>
                {other}: {affinityWord(tie.affinity)} ({signed(tie.affinity)})
              </summary>
              <ol className="character-history">
                {[...tie.history].reverse().map((e, i) => (
                  <li key={`${e.at}-${i}`}>
                    <span className={e.change > 0 ? 'up' : 'down'}>{signed(e.change)}</span> {e.text} <span className="hint">{formatMoment(e.at)}</span>
                  </li>
                ))}
              </ol>
            </details>
          ))
        )}
      </section>

      {change.error && <p className="characters-error" role="alert">{change.error.message}</p>}
    </article>
  )
}
