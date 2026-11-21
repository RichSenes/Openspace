import { useState, type FormEvent } from 'react'
import { ArrowDown, ArrowUp, Plus, X } from 'lucide-react'
import { ApiError } from '../api/client'
import { INITIALIZABLE, useEngines, useSaveOffice, type OfficeSaveFailure } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { useToast } from '../ui/toastContext'
import { changedParts, formOf, move, pathsOf, reserveChoices, type OfficeForm, type OfficePart } from './officeForm'
import { MAX_RESERVE_MODELS, unknownModels } from './reserve'
import './office.css'

const PART_LABEL: Record<OfficePart, string> = {
  name: 'Name',
  repository: 'Repository',
  localOnly: 'Kept on this machine',
  holdOverlaps: 'Overlapping work',
  privatePaths: 'Private folders',
  reserveModels: 'Reserve models',
}

/**
 * The office's own settings, apart from its rooms: name, repository, what stays on this
 * machine, the folders no agent may touch and the models that go on when an account is over
 * its limits. One form, one save.
 */
export function OfficePanel({ office }: { office: OfficeView }) {
  const [form, setForm] = useState<OfficeForm>(() => formOf(office))
  const [failed, setFailed] = useState<OfficeSaveFailure[]>([])
  const save = useSaveOffice(office.id)
  const toast = useToast()
  const parts = changedParts(office, form)
  const set = <K extends keyof OfficeForm>(key: K, value: OfficeForm[K]) => setForm((f) => ({ ...f, [key]: value }))
  const tooMany = form.reserveModels.length > MAX_RESERVE_MODELS

  const run = (initialize: boolean) =>
    save.mutate(
      { form, parts: initialize ? ['repository'] : parts, initialize },
      {
        onSuccess: (problems) => {
          setFailed(problems)
          if (problems.length === 0) toast(initialize ? 'Git initialized and repository saved' : 'Office saved')
        },
      },
    )

  const submit = (e: FormEvent) => {
    e.preventDefault()
    run(false)
  }

  const problemOf = (part: OfficePart) => failed.find((f) => f.part === part)?.error
  const repositoryProblem = problemOf('repository')
  const canInitialize = repositoryProblem instanceof ApiError && repositoryProblem.code !== undefined && INITIALIZABLE.has(repositoryProblem.code)

  return (
    <form className="office-page" onSubmit={submit}>
      <header className="office-head">
        <div>
          <h1>{office.name}</h1>
          <p className="hint">The office&apos;s own settings. Its rooms and desks are in Rooms.</p>
        </div>
        <div className="row">
          <button type="button" className="btn btn-quiet" disabled={parts.length === 0 || save.isPending} onClick={() => { setForm(formOf(office)); setFailed([]) }}>
            Undo changes
          </button>
          <button type="submit" className="btn btn-primary" disabled={parts.length === 0 || tooMany || !form.name.trim() || save.isPending}>
            {save.isPending ? 'Saving…' : 'Save office'}
          </button>
        </div>
        {failed.length > 0 && (
          <p className="error office-failed">
            Not saved: {failed.map((f) => PART_LABEL[f.part]).join(', ')}. The rest is saved; the reason is under each field.
          </p>
        )}
      </header>

      <div className="office-parts">
        <section className="office-part" aria-labelledby="office-project">
          <h2 id="office-project">The project</h2>
          <label className="field">
            <span>Name</span>
            <input className="input" value={form.name} onChange={(e) => set('name', e.target.value)} required />
            {problemOf('name') && <span className="error">{problemOf('name')!.message}</span>}
          </label>
          <label className="field">
            <span>Repository folder</span>
            <input
              className="input mono"
              value={form.repositoryPath}
              onChange={(e) => set('repositoryPath', e.target.value)}
              placeholder="/home/you/repos/project"
            />
            <span className="hint">
              The absolute path of the git repository the agents work on. Each task works on a copy of it and comes back as a branch.
              {!office.repositoryPath && ' Until it is set, tasks of this office cannot start.'}
            </span>
          </label>
          {repositoryProblem && (
            <div className="repo-problem">
              <p className="error">{repositoryProblem.message}</p>
              {canInitialize && (
                <button type="button" className="btn btn-primary" disabled={save.isPending} onClick={() => run(true)}>
                  Initialize git here
                </button>
              )}
            </div>
          )}
        </section>

        <section className="office-part" aria-labelledby="office-overlaps">
          <h2 id="office-overlaps">Work on the same files</h2>
          <label className="check-row">
            <input type="checkbox" checked={form.holdOverlaps} onChange={(e) => set('holdOverlaps', e.target.checked)} />
            <span>
              Hold a task while another changes its files
              <span className="hint">A queued task that names files another desk is changing waits, and the board says for whom. Tasks that name no files never wait.</span>
            </span>
          </label>
          {problemOf('holdOverlaps') && <p className="error">{problemOf('holdOverlaps')!.message}</p>}
        </section>

        <section className="office-part" aria-labelledby="office-machine">
          <h2 id="office-machine">What stays on this machine</h2>
          <label className="check-row">
            <input type="checkbox" checked={form.localOnly} onChange={(e) => set('localOnly', e.target.checked)} />
            <span>
              Keep the repository on this machine
              <span className="hint">Openspace never pushes it or opens pull requests. The desks&apos; models still read the code.</span>
            </span>
          </label>
          {problemOf('localOnly') && <p className="error">{problemOf('localOnly')!.message}</p>}
          <label className="field">
            <span>Private folders</span>
            <textarea
              className="input mono"
              value={form.privatePaths}
              onChange={(e) => set('privatePaths', e.target.value)}
              placeholder={'/home/you/photos\n/home/you/ai/models'}
            />
            <span className="hint">
              One absolute path per line ({pathsOf(form.privatePaths).length} now). No agent of this office may open, list, read, search or
              change them: Openspace refuses any request that names them without asking you, and each agent is told so.
            </span>
            {problemOf('privatePaths') && <span className="error">{problemOf('privatePaths')!.message}</span>}
          </label>
        </section>

        <ReserveModels models={form.reserveModels} onChange={(m) => set('reserveModels', m)} problem={problemOf('reserveModels')} />
      </div>
    </form>
  )
}

/** The models that go on with tasks while a desk's account is over its limits, in order, picked from a list. */
function ReserveModels({ models, onChange, problem }: { models: string[]; onChange: (models: string[]) => void; problem?: Error }) {
  const engines = useEngines().data
  const choices = reserveChoices(engines?.openCodeModels, engines?.claudeCode ? engines.claudeCodeModels : undefined)
  const [adding, setAdding] = useState('')
  const unknown = unknownModels(models, engines?.openCodeModels, engines?.claudeCode ? engines.claudeCodeModels : undefined)
  const nameOf = (id: string) => choices.find((c) => c.id === id)?.name ?? id
  const add = () => {
    const model = adding.trim()
    if (model && !models.includes(model)) onChange([...models, model])
    setAdding('')
  }

  return (
    <section className="office-part" aria-labelledby="office-reserves">
      <h2 id="office-reserves">Reserve models</h2>
      <p className="hint">
        When a desk&apos;s account is over its limits, its tasks go on with the first of these on another account that is free, and go back to
        the desk when its account is. Free tiers may keep and train on what they are sent: use them only for code you would publish.
      </p>
      {models.length === 0 ? (
        <p className="hint">None: tasks wait for their desk&apos;s account.</p>
      ) : (
        <ol className="reserve-list">
          {models.map((m, i) => (
            <li key={m}>
              <span className="reserve-order">{i + 1}</span>
              <span className="reserve-name">
                <span>{nameOf(m)}</span>
                {nameOf(m) !== m && <span className="hint mono">{m}</span>}
                {unknown.includes(m) && <span className="hint">Nobody lists it: check the name, or connect that provider in OpenCode.</span>}
              </span>
              <button type="button" className="btn btn-quiet" disabled={i === 0} onClick={() => onChange(move(models, i, -1))} aria-label={`Move ${m} up`}>
                <ArrowUp size={16} aria-hidden />
              </button>
              <button
                type="button"
                className="btn btn-quiet"
                disabled={i === models.length - 1}
                onClick={() => onChange(move(models, i, 1))}
                aria-label={`Move ${m} down`}
              >
                <ArrowDown size={16} aria-hidden />
              </button>
              <button type="button" className="btn btn-quiet" onClick={() => onChange(models.filter((x) => x !== m))} aria-label={`Remove ${m}`}>
                <X size={16} aria-hidden />
              </button>
            </li>
          ))}
        </ol>
      )}
      {models.length < MAX_RESERVE_MODELS ? (
        <div className="reserve-add">
          <label className="field">
            <span>Add a model</span>
            <select className="input" value={adding} onChange={(e) => setAdding(e.target.value)}>
              <option value="">Choose…</option>
              {choices
                .filter((c) => !models.includes(c.id))
                .map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
            </select>
          </label>
          <button type="button" className="btn" disabled={!adding} onClick={add}>
            <Plus size={16} aria-hidden /> Add
          </button>
          {choices.length === 0 && <p className="hint">No engine lists its models now: start opencode serve, or install Claude Code.</p>}
        </div>
      ) : (
        <p className="hint">At most {MAX_RESERVE_MODELS} reserve models: remove one to add another.</p>
      )}
      {problem && <p className="error">{problem.message}</p>}
    </section>
  )
}
