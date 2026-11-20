import { useEffect, useState, type FormEvent } from 'react'
import { useEngines, useSaveSettings, useSettings, useBackUp, useBackups } from '../api/hooks'
import type { Autonomy, SettingsChange, SettingsView } from '../api/types'
import { ClaudeUsageMeters } from '../engines/ClaudeUsage'
import { useToast } from '../ui/toastContext'
import { formatMoment } from '../ui/dates'
import { GoogleCard } from './GoogleCard'
import { LocalAiSection } from './LocalAiSection'
import { ModelsSection } from './ModelsSection'
import { SoundsCard } from './SoundsCard'
import { WeatherCard } from './WeatherCard'
import { VersionCard } from './VersionCard'
import { usePersistentState } from '../usePersistentState'
import { isSection, onSectionRequest, requestSettingsSection, SECTION_KEY, SECTIONS, type SettingsSection } from './sections'
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
 * local AI, models per function, how agents work, connections, look and sound, the system. One section shows at a time.
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
          {section === 'local' && <LocalAiSection />}
          {section === 'models' && <ModelsSection />}
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

/** Permissions and seats; who plans and reviews is in Settings › Models, and is left as it is here. */
function SettingsForm({ saved }: { saved: SettingsView }) {
  const [form, setForm] = useState<SettingsChange>({ autonomy: saved.autonomy, seats: saved.seats })
  const save = useSaveSettings()
  const toast = useToast()
  const set = (change: Partial<SettingsChange>) => setForm({ ...form, ...change })
  const changed = form.autonomy !== saved.autonomy || form.seats !== saved.seats

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate(form, { onSuccess: () => toast('Settings saved: they apply now') })
  }

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

      <p className="hint">
        Who plans requests and who reviews finished work is chosen in{' '}
        <button type="button" className="link-button" onClick={() => requestSettingsSection('models')}>
          Models
        </button>
        .
      </p>

      {save.error && <p className="error">{save.error.message}</p>}
      <div className="row settings-actions">
        <button type="submit" className="btn btn-primary" disabled={!changed || save.isPending}>
          Save settings
        </button>
        <button type="button" className="btn btn-quiet" disabled={!changed} onClick={() => setForm({ autonomy: saved.autonomy, seats: saved.seats })}>
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
