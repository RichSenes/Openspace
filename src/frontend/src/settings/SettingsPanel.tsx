import { useEffect, useState, type FormEvent } from 'react'
import { useEngines, useSaveSettings, useSettings, useBackUp, useBackups } from '../api/hooks'
import type { Autonomy, SettingsView } from '../api/types'
import { ClaudeUsageMeters } from '../engines/ClaudeUsage'
import { useToast } from '../ui/toastContext'
import { formatMoment } from '../ui/dates'
import { GoogleCard } from './GoogleCard'
import { SoundsCard } from './SoundsCard'
import { WeatherCard } from './WeatherCard'
import { VersionCard } from './VersionCard'
import { usePersistentState } from '../usePersistentState'
import { isSection, onSectionRequest, SECTION_KEY, SECTIONS, type SettingsSection } from './sections'
import './settings.css'

const MAX_SEATS = 12

const AUTONOMY: { value: Autonomy; label: string; hint: string }[] = [
  {
    value: 'Auto',
    label: 'Auto: only risky requests wait for you',
    hint: 'Openspace answers agents on its own while they work inside their task’s copy. It still asks you before pushing or deploying, changing the machine, going outside the copy, deleting outside it, or touching keys and .env files.',
  },
  {
    value: 'Ask',
    label: 'Ask: every request waits for you',
    hint: 'Whatever a role does not allow outright waits for your answer, one request at a time.',
  },
]

/**
 * What a person sets for the whole of Openspace, in sections with an index: the engines,
 * how agents work, connections, look and sound, the system. One section shows at a time.
 */
export function SettingsPanel() {
  const settings = useSettings()
  const [stored, setSection] = usePersistentState<string>(SECTION_KEY, 'engines')
  const section: SettingsSection = isSection(stored) ? stored : 'engines'
  useEffect(() => onSectionRequest(setSection), [setSection])
  const current = SECTIONS.find((s) => s.id === section)!

  return (
    <div className="settings-room">
      <section className="settings">
        <header className="settings-head">
          <h1>Settings</h1>
          <p className="hint">They apply at once, to every office, and stay after a restart.</p>
        </header>

        <nav className="settings-index" aria-label="Settings sections">
          {SECTIONS.map((s) => (
            <button key={s.id} type="button" aria-current={s.id === section ? 'true' : undefined} onClick={() => setSection(s.id)}>
              <span>{s.label}</span>
              <span className="hint">{s.hint}</span>
            </button>
          ))}
        </nav>

        <div className="settings-body" aria-label={current.label}>
          <h2 className="settings-section-title">{current.label}</h2>
          {section === 'engines' && (
            <div className="settings-cards">
              <EnginesStatus />
              <section className="settings-card" aria-labelledby="claude-account">
                <h3 id="claude-account">Claude account</h3>
                <ClaudeUsageMeters />
              </section>
            </div>
          )}
          {section === 'agents' && (
            <>
              {settings.error && <p className="error">Could not read the settings: {settings.error.message}</p>}
              {settings.data && <SettingsForm key={JSON.stringify(settings.data)} saved={settings.data} />}
            </>
          )}
          {section === 'connections' && (
            <div className="settings-cards">
              <GoogleCard />
              <section className="settings-card" aria-labelledby="telegram">
                <h3 id="telegram">Telegram</h3>
                <p className="hint">
                  Routines write to Telegram with bots named in <code className="mono">~/.config/openspace/telegram.env</code> and the group in{' '}
                  <code className="mono">telegram.json</code>, next to it. A guided setup will come here.
                </p>
              </section>
            </div>
          )}
          {section === 'look' && (
            <div className="settings-cards">
              <SoundsCard />
              <WeatherCard />
            </div>
          )}
          {section === 'system' && (
            <div className="settings-cards">
              <VersionCard />
              <Backups />
            </div>
          )}
        </div>
      </section>
    </div>
  )
}


/** Copies of the database, taken every day by themselves; restored from the command line. */
function Backups() {
  const backups = useBackups()
  const backUp = useBackUp()
  const toast = useToast()
  const latest = backups.data?.[0]
  return (
    <section className="settings-card" aria-labelledby="backups">
      <h3 id="backups">Backups</h3>
      <p className="hint">
        Openspace copies its database once a day while it runs, and keeps the last 14 copies. Tasks, logs, reports and offices are all in
        it; your repositories are not.
      </p>
      <p>{latest ? `Latest copy: ${formatMoment(latest.createdAt)}, ${(latest.size / 1_048_576).toFixed(1)} MB.` : 'No copy yet.'}</p>
      <div className="row">
        <button type="button" className="btn" disabled={backUp.isPending} onClick={() => backUp.mutate(undefined, { onSuccess: () => toast('Database copied') })}>
          Back up now
        </button>
      </div>
      {backUp.error && <p className="error">{backUp.error.message}</p>}
      {latest && (
        <p className="hint">
          To go back to a copy, run <code className="mono">scripts/openspace restore {latest.path}</code> from the Openspace folder: it stops the
          API, keeps the current database aside and starts again from the copy.
        </p>
      )}
    </section>
  )
}

function SettingsForm({ saved }: { saved: SettingsView }) {
  const [form, setForm] = useState(saved)
  const engines = useEngines().data
  const save = useSaveSettings()
  const toast = useToast()
  const claude = engines?.claudeCode ?? false
  const set = (change: Partial<SettingsView>) => setForm({ ...form, ...change })
  const changed = JSON.stringify(form) !== JSON.stringify(saved)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate(form, { onSuccess: () => toast('Settings saved: they apply now') })
  }

  const claudeModel = (value: string | null, onChange: (model: string | null) => void, label: string) => (
    <label className="field">
      <span>{label}</span>
      <select className="input" value={value ?? ''} disabled={!claude} onChange={(e) => onChange(e.target.value || null)}>
        <option value="">Claude Code&apos;s default</option>
        {engines?.claudeCodeModels.map((m) => (
          <option key={m.id} value={m.id}>
            {m.name}
          </option>
        ))}
      </select>
    </label>
  )

  const openCodeModel = (value: string | null, onChange: (model: string | null) => void, label: string, none: string) => (
    <label className="field">
      <span>{label}</span>
      <select className="input" value={value ?? ''} onChange={(e) => onChange(e.target.value || null)}>
        <option value="">{none}</option>
        {value && !engines?.openCodeModels.some((m) => m.id === value) && <option value={value}>{value} (not listed now)</option>}
        {engines?.openCodeModels.map((m) => (
          <option key={m.id} value={m.id}>
            {m.name}
          </option>
        ))}
      </select>
    </label>
  )

  return (
    <form className="settings-form" onSubmit={submit}>
      <section className="settings-card" role="radiogroup" aria-labelledby="permissions">
        <h3 id="permissions">Permissions</h3>
        {AUTONOMY.map((a) => (
          <label key={a.value} className="setting-choice">
            <input type="radio" name="autonomy" value={a.value} checked={form.autonomy === a.value} onChange={() => set({ autonomy: a.value })} />
            <span>
              <strong>{a.label}</strong>
              <span className="hint">{a.hint}</span>
            </span>
          </label>
        ))}
        <p className="hint">Roles still decide what an agent may do at all: what a role denies stays denied.</p>
      </section>

      <section className="settings-card" aria-labelledby="seats">
        <h3 id="seats">Agents at work</h3>
        <label className="field seats">
          <span>How many agents work at the same time</span>
          <input
            className="input"
            type="number"
            min={1}
            max={MAX_SEATS}
            value={form.seats}
            onChange={(e) => set({ seats: Number(e.target.value) })}
            required
          />
        </label>
        <p className="hint">Each desk works on one task at a time; more seats let more desks work together, and spend your limits faster.</p>
      </section>

      <section className="settings-card" aria-labelledby="supervisor">
        <h3 id="supervisor">Supervisor and reviewer</h3>
        {!claude && <p className="hint">Claude Code is not on this machine: OpenCode plans, and reviews when you choose a model for it below.</p>}
        <label className="setting-choice">
          <input type="checkbox" checked={claude && form.claudeSupervises} disabled={!claude} onChange={(e) => set({ claudeSupervises: e.target.checked })} />
          <span>
            <strong>Claude Code plans requests and drafts texts</strong>
            <span className="hint">Otherwise OpenCode does, with the model below.</span>
          </span>
        </label>
        {claudeModel(form.supervisorModel, (supervisorModel) => set({ supervisorModel }), 'Model for plans and drafts')}
        <label className="setting-choice">
          <input type="checkbox" checked={claude && form.claudeReviews} disabled={!claude} onChange={(e) => set({ claudeReviews: e.target.checked })} />
          <span>
            <strong>Claude Code reviews finished work before you</strong>
            <span className="hint">It reads the changes, leaves its verdict as a note and lists what to try by hand.</span>
          </span>
        </label>
        {claudeModel(form.reviewModel, (reviewModel) => set({ reviewModel }), 'Model for reviews')}
        {openCodeModel(form.draftModel ?? null, (draftModel) => set({ draftModel }), 'OpenCode model for plans and drafts, when Claude Code does not make them', "OpenCode's default")}
        {openCodeModel(
          form.openCodeReviewModel ?? null,
          (openCodeReviewModel) => set({ openCodeReviewModel }),
          'OpenCode model for reviews, when Claude Code does not review',
          'No review by OpenCode',
        )}
        <p className="hint">
          With a model of your company&apos;s subscription (GitHub Copilot&apos;s, say, once signed in to it in OpenCode), plans and reviews stay
          on what your company allows. An OpenCode reviewer reads the changes and the files, and runs nothing.
        </p>
      </section>

      {save.error && <p className="error">{save.error.message}</p>}
      <div className="row settings-actions">
        <button type="submit" className="btn btn-primary" disabled={!changed || save.isPending}>
          Save settings
        </button>
        <button type="button" className="btn btn-quiet" disabled={!changed} onClick={() => setForm(saved)}>
          Undo changes
        </button>
      </div>
    </form>
  )
}

/** What runs the agents here, and where each engine keeps its keys: never in Openspace. */
function EnginesStatus() {
  const engines = useEngines().data
  return (
    <section className="settings-card" aria-labelledby="engines">
      <h3 id="engines">Status and keys</h3>
      <p className="hint">Desks run on one of these. Openspace keeps no keys or passwords: each engine keeps its own, on this machine.</p>
      <dl className="accounts">
        <dt>OpenCode</dt>
        <dd>
          <span className={engines?.openCode ? 'engine-ok' : 'engine-missing'}>
            {engines ? (engines.openCode ? `Reachable, with ${engines.openCodeModels.length} models.` : 'Not reachable: start opencode serve.') : 'Asking…'}
          </span>
          <span className="hint">
            Add a provider and its key with <code className="mono">opencode auth login</code>, then restart <code className="mono">opencode serve</code>.
          </span>
        </dd>
        <dt>Claude Code</dt>
        <dd>
          <span className={engines?.claudeCode ? 'engine-ok' : 'engine-missing'}>
            {engines ? (engines.claudeCode ? 'Ready, signed in with your Claude account.' : 'Not found on this machine.') : 'Asking…'}
          </span>
          <span className="hint">
            Install it, then sign in or change account by running <code className="mono">claude</code> and typing <code className="mono">/login</code>. It is
            optional: OpenCode alone runs every desk.
          </span>
        </dd>
      </dl>
    </section>
  )
}
