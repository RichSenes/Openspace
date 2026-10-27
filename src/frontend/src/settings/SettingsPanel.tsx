import { useState, type FormEvent } from 'react'
import { useEngines, useSaveSettings, useSettings, useBackUp, useBackups } from '../api/hooks'
import type { Autonomy, SettingsView } from '../api/types'
import { ClaudeUsageMeters } from '../engines/ClaudeUsage'
import { useToast } from '../ui/toastContext'
import { GoogleCard } from './GoogleCard'
import { SoundsCard } from './SoundsCard'
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

/** What a person sets for the whole of Openspace: how free agents are, how many work, who plans and reviews. */
export function SettingsPanel() {
  const settings = useSettings()
  return (
    <section className="settings">
      <header className="settings-head">
        <h1>Settings</h1>
        <p className="hint">They apply at once, to every office, and stay after a restart.</p>
      </header>
      {settings.error && <p className="error">Could not read the settings: {settings.error.message}</p>}
      {settings.data && <SettingsForm key={JSON.stringify(settings.data)} saved={settings.data} />}
      <section className="settings-card" aria-labelledby="claude-account">
        <h2 id="claude-account">Claude account</h2>
        <ClaudeUsageMeters />
      </section>
      <SoundsCard />
      <Backups />
      <GoogleCard />
      <Accounts />
    </section>
  )
}

const when = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/** Copies of the database, taken every day by themselves; restored from the command line. */
function Backups() {
  const backups = useBackups()
  const backUp = useBackUp()
  const toast = useToast()
  const latest = backups.data?.[0]
  return (
    <section className="settings-card" aria-labelledby="backups">
      <h2 id="backups">Backups</h2>
      <p className="hint">
        Openspace copies its database once a day while it runs, and keeps the last 14 copies. Tasks, logs, reports and offices are all in
        it; your repositories are not.
      </p>
      <p>{latest ? `Latest copy: ${when(latest.createdAt)}, ${(latest.size / 1_048_576).toFixed(1)} MB.` : 'No copy yet.'}</p>
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

  return (
    <form className="settings-form" onSubmit={submit}>
      <fieldset className="settings-card">
        <legend>
          <h2>Permissions</h2>
        </legend>
        {AUTONOMY.map((a) => (
          <label key={a.value} className="choice">
            <input type="radio" name="autonomy" value={a.value} checked={form.autonomy === a.value} onChange={() => set({ autonomy: a.value })} />
            <span>
              <strong>{a.label}</strong>
              <span className="hint">{a.hint}</span>
            </span>
          </label>
        ))}
        <p className="hint">Roles still decide what an agent may do at all: what a role denies stays denied.</p>
      </fieldset>

      <fieldset className="settings-card">
        <legend>
          <h2>Agents at work</h2>
        </legend>
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
      </fieldset>

      <fieldset className="settings-card">
        <legend>
          <h2>Supervisor and reviewer</h2>
        </legend>
        {!claude && <p className="hint">Claude Code is not on this machine: OpenCode plans, and nobody reviews before you.</p>}
        <label className="choice">
          <input type="checkbox" checked={form.claudeSupervises} disabled={!claude} onChange={(e) => set({ claudeSupervises: e.target.checked })} />
          <span>
            <strong>Claude Code plans requests and drafts texts</strong>
            <span className="hint">Otherwise OpenCode does, with the model below.</span>
          </span>
        </label>
        {claudeModel(form.supervisorModel, (supervisorModel) => set({ supervisorModel }), 'Model for plans and drafts')}
        <label className="choice">
          <input type="checkbox" checked={form.claudeReviews} disabled={!claude} onChange={(e) => set({ claudeReviews: e.target.checked })} />
          <span>
            <strong>Claude Code reviews finished work before you</strong>
            <span className="hint">It reads the changes, leaves its verdict as a note and lists what to try by hand.</span>
          </span>
        </label>
        {claudeModel(form.reviewModel, (reviewModel) => set({ reviewModel }), 'Model for reviews')}
        <label className="field">
          <span>OpenCode model for plans and drafts, when Claude Code does not make them</span>
          <select className="input" value={form.draftModel ?? ''} onChange={(e) => set({ draftModel: e.target.value || null })}>
            <option value="">OpenCode&apos;s default</option>
            {form.draftModel && !engines?.openCodeModels.some((m) => m.id === form.draftModel) && (
              <option value={form.draftModel}>{form.draftModel} (not listed now)</option>
            )}
            {engines?.openCodeModels.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
          </select>
        </label>
      </fieldset>

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

/** Where keys live: never in Openspace, always in the engine that uses them. */
function Accounts() {
  const engines = useEngines().data
  return (
    <section className="settings-card" aria-labelledby="accounts">
      <h2 id="accounts">Keys and accounts</h2>
      <p className="hint">Openspace keeps no keys or passwords: each engine keeps its own, on this machine.</p>
      <dl className="accounts">
        <dt>OpenCode</dt>
        <dd>
          {engines?.openCode ? `Reachable, with ${engines.openCodeModels.length} models.` : 'Not reachable: start opencode serve.'} Add a provider and its
          key with <code className="mono">opencode auth login</code>, then restart <code className="mono">opencode serve</code>.
        </dd>
        <dt>Claude Code</dt>
        <dd>
          {engines?.claudeCode ? 'Ready, signed in with your Claude account.' : 'Not found on this machine.'} Sign in or change account by running{' '}
          <code className="mono">claude</code> and typing <code className="mono">/login</code>.
        </dd>
      </dl>
    </section>
  )
}
