import { useState, type FormEvent } from 'react'
import { Plus, Sparkles } from 'lucide-react'
import { useDeleteRole, useDraftRole, useEngines, useRoles, useSaveRole } from '../api/hooks'
import type { PermissionLevel, RoleInput, RoleView } from '../api/types'
import { modelOptions } from '../engines/engines'
import { EnginesCard } from '../engines/EnginesCard'
import { DeskUsage } from './DeskUsage'
import { useToast } from '../ui/toastContext'
import { CAPABILITIES, formFrom, HELPER_LABEL, PERMISSION_LABEL, permissionSummary } from './roleForm'
import './agents.css'

/** Defines the kinds of agents desks can host: what they do, how, and what they may do alone. */
export function AgentsPanel() {
  const roles = useRoles()
  const [selected, setSelected] = useState<string | 'new'>('new')
  const current = roles.data?.find((r) => r.id === selected)

  return (
    <section className="agents">
      <header className="agents-head">
        <h1>Agents</h1>
        <p className="hint">
          A role tells an agent what it is for, how to work and what it may do without asking. Give roles to desks in Rooms.
        </p>
        <EnginesCard />
      </header>

      <div className="agents-body">
        <nav className="role-list" aria-label="Roles">
          <button type="button" className="btn" onClick={() => setSelected('new')} aria-pressed={selected === 'new'}>
            <Plus size={16} /> New role
          </button>
          {roles.data?.length === 0 && <p className="hint">No roles yet. Desks without a role use OpenCode&apos;s agents as they are.</p>}
          <ul>
            {roles.data?.map((r) => (
              <li key={r.id}>
                <button type="button" className="role-item" aria-current={r.id === selected ? 'true' : undefined} onClick={() => setSelected(r.id)}>
                  <span className="role-name">{r.name}</span>
                  {r.description && <span className="hint">{r.description}</span>}
                  <span className="hint">{permissionSummary(r)}</span>
                </button>
              </li>
            ))}
          </ul>
        </nav>

        <RoleEditor key={current?.id ?? 'new'} role={current} onSaved={setSelected} onDeleted={() => setSelected('new')} />
      </div>

      <DeskUsage />
    </section>
  )
}

interface EditorProps {
  role: RoleView | undefined
  onSaved: (id: string) => void
  onDeleted: () => void
}

function RoleEditor({ role, onSaved, onDeleted }: EditorProps) {
  const [form, setForm] = useState<RoleInput>(() => formFrom(role))
  const save = useSaveRole()
  const remove = useDeleteRole()
  const draft = useDraftRole()
  const engines = useEngines()
  const toast = useToast()
  const set = <K extends keyof RoleInput>(key: K, value: RoleInput[K]) => setForm((f) => ({ ...f, [key]: value }))

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate(
      { id: role?.id ?? null, role: { ...form, model: form.model?.trim() || null } },
      {
        onSuccess: ({ id }) => {
          toast(role ? 'Role saved' : `${form.name.trim()} created`)
          onSaved(id)
        },
      },
    )
  }

  return (
    <form className="role-editor" onSubmit={submit}>
      <h2>{role ? `Edit ${role.name}` : 'New role'}</h2>

      <div className="role-grid">
        <label className="field">
          <span>Name</span>
          <input className="input" value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="e.g. Accessibility reviewer" required />
        </label>
        <label className="field">
          <span>OpenCode model (desks can override it; Claude Code desks use their own)</span>
          <select className="input" value={form.model ?? ''} onChange={(e) => set('model', e.target.value)}>
            <option value="">The agent&apos;s default</option>
            {modelOptions(engines.data, 'OpenCode', form.model ?? '').map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
          </select>
        </label>
      </div>

      <label className="field">
        <span>What it does, in one line</span>
        <input className="input" value={form.description} onChange={(e) => set('description', e.target.value)} placeholder="e.g. Checks pages against WCAG AA and fixes what fails" />
      </label>

      <div className="field">
        <div className="instructions-head">
          <span className="hint">Instructions: how it works and what it must never do</span>
          <button
            type="button"
            className="btn btn-quiet"
            disabled={!form.description.trim() || draft.isPending}
            onClick={() => draft.mutate({ name: form.name || 'Agent', description: form.description })}
          >
            <Sparkles size={16} /> {draft.isPending ? 'Drafting…' : 'Draft with AI'}
          </button>
        </div>
        <textarea
          className="input instructions"
          value={form.instructions}
          onChange={(e) => set('instructions', e.target.value)}
          placeholder="- Work only on the files of this room&#10;- Never change the payment module&#10;- Run the tests before finishing"
          aria-label="Instructions"
        />
        {draft.error && <p className="error">{draft.error.message}</p>}
        {draft.data && (
          <div className="draft">
            <p className="hint">Draft from the AI. Read it before using it.</p>
            <pre>{draft.data}</pre>
            <div className="row">
              <button type="button" className="btn btn-primary" onClick={() => { set('instructions', draft.data); draft.reset() }}>
                Use this draft
              </button>
              <button type="button" className="btn btn-quiet" onClick={() => draft.reset()}>
                Throw it away
              </button>
            </div>
          </div>
        )}
      </div>

      <fieldset className="permissions">
        <legend className="hint">What it may do without asking</legend>
        {CAPABILITIES.map((c) => (
          <label key={c.key} className="field">
            <span>{c.label}</span>
            <select className="input" value={form[c.key]} onChange={(e) => set(c.key, e.target.value as PermissionLevel)}>
              {(Object.keys(PERMISSION_LABEL) as PermissionLevel[]).map((level) => (
                <option key={level} value={level}>
                  {PERMISSION_LABEL[level]}
                </option>
              ))}
            </select>
          </label>
        ))}
        <label className="field">
          <span>Call in interns</span>
          <select className="input" value={form.helpers === 'Allow' ? 'Allow' : 'Deny'} onChange={(e) => set('helpers', e.target.value as PermissionLevel)}>
            {(Object.keys(HELPER_LABEL) as PermissionLevel[]).map((level) => (
              <option key={level} value={level}>
                {HELPER_LABEL[level]}
              </option>
            ))}
          </select>
          <span className="hint">
            Claude Code agents may hand parts of the work to helpers, who show up as interns beside the desk. Each one uses your Claude
            plan and follows the same rules as the desk. OpenCode agents always can.
          </span>
        </label>
      </fieldset>

      {(save.error ?? remove.error) && <p className="error">{(save.error ?? remove.error)!.message}</p>}
      <div className="row">
        <button type="submit" className="btn btn-primary" disabled={save.isPending}>
          {role ? 'Save role' : 'Create role'}
        </button>
        {role && (
          <button
            type="button"
            className="btn btn-danger"
            disabled={remove.isPending}
            onClick={() => remove.mutate(role.id, { onSuccess: () => { toast('Role deleted'); onDeleted() } })}
          >
            Delete role
          </button>
        )}
      </div>
    </form>
  )
}
