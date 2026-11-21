import { useState, type FormEvent, type ReactNode } from 'react'
import { useEngines, useModels, useSaveModels } from '../api/hooks'
import type { EngineChoice, FunctionChoice, ModelEngine, ModelsSettings, ModelsView } from '../api/types'
import { useToast } from '../ui/toastContext'
import { changed, DEFAULT_MODEL, freeModelsText, isUnknown, listOf, parseFreeModels, problemOf, runsOn, settingsOf, withEngine, type ModelLists } from './models'
import { requestSettingsSection } from './sections'

/**
 * Which engine and model does each job: Shiori, the reviewer, digests, journals. One form,
 * one save; the decider is only shown, since Local AI sets it.
 */
export function ModelsSection() {
  const models = useModels()
  if (models.error) return <p className="error">Could not read the models: {models.error.message}</p>
  if (!models.data) return <p className="hint">Reading the models…</p>
  return <ModelsForm key={JSON.stringify(settingsOf(models.data))} view={models.data} />
}

function ModelsForm({ view }: { view: ModelsView }) {
  const [choices, setDraft] = useState<ModelsSettings>(() => settingsOf(view))
  // The field keeps what is typed, blank lines included; the list sent is read from it.
  const [freeText, setFreeText] = useState(() => freeModelsText(view.freeModels))
  const draft: ModelsSettings = { ...choices, freeModels: parseFreeModels(freeText) }
  const engines = useEngines().data
  const save = useSaveModels()
  const toast = useToast()
  const claude = engines?.claudeCode ?? false
  const lists: ModelLists = {
    claudeCode: engines?.claudeCode ? engines.claudeCodeModels : undefined,
    openCode: engines?.openCodeModels,
    local: view.localModels,
  }
  const set = (change: Partial<ModelsSettings>) => setDraft((d) => ({ ...d, ...change }))
  const dirty = changed(view, draft)
  const problem = problemOf(draft)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate(draft, { onSuccess: () => toast('Models saved: they apply now') })
  }

  return (
    <form className="settings-form" onSubmit={submit}>
      {engines && !claude && (
        <p className="hint">Claude Code is not on this machine: where it is chosen, OpenCode stands in with the model under it.</p>
      )}

      <section className="settings-card" aria-labelledby="models-shiori">
        <h3 id="models-shiori">Shiori</h3>
        <p className="hint">Plans the requests you make and drafts texts today; later it is also who you chat with.</p>
        <TwoEngines
          name="shiori"
          choice={draft.shiori}
          onChange={(shiori) => set({ shiori })}
          lists={lists}
          claude={claude}
          claudeHint="Opus, the alias, is always the latest Opus."
          openCodeLabel={(cc) => (cc ? 'OpenCode model, when Claude Code is not on this machine' : 'OpenCode model')}
          openCodeNone="OpenCode’s default"
        />
      </section>

      <section className="settings-card" aria-labelledby="models-reviewer">
        <h3 id="models-reviewer">Reviewer</h3>
        <p className="hint">Reads finished work before you, leaves its verdict as a note and lists what to try by hand.</p>
        <TwoEngines
          name="reviewer"
          choice={draft.reviewer}
          onChange={(reviewer) => set({ reviewer })}
          lists={lists}
          claude={claude}
          claudeHint="Claude Code reads the changes and may run the checks."
          openCodeLabel={(cc) => (cc ? 'OpenCode model, when Claude Code is not on this machine' : 'OpenCode model')}
          openCodeNone="No review by OpenCode"
          noneLabel="No review"
        />
        <p className="hint">
          With a model of your company&apos;s subscription (GitHub Copilot&apos;s, say, once signed in to it in OpenCode), plans and reviews stay on
          what your company allows. An OpenCode reviewer reads the changes and the files, and runs nothing.
        </p>
      </section>

      <section className="settings-card" aria-labelledby="models-digests">
        <h3 id="models-digests">Digests and summaries</h3>
        <p className="hint">The weekly and monthly digests of the brain, and the summaries of its wiki pages.</p>
        <OneEngine name="digests" choice={draft.digests} onChange={(digests) => set({ digests })} lists={lists} claude={claude} />
      </section>

      <section className="settings-card" aria-labelledby="models-journals">
        <h3 id="models-journals">Journals and chronicle</h3>
        <p className="hint">The inhabitants&apos; journals, the chronicle of the office and the lines of the simulation: many short texts, best on a local model that spends no quota.</p>
        <OneEngine name="journals" choice={draft.journals} onChange={(journals) => set({ journals })} lists={lists} claude={claude} />
      </section>

      <section className="settings-card" aria-labelledby="models-free">
        <h3 id="models-free">Free models</h3>
        <label className="field">
          <span>OpenCode models you know are free, one per line</span>
          <textarea
            className="input mono"
            value={freeText}
            onChange={(e) => setFreeText(e.target.value)}
            placeholder={'opencode/some-model\nopenrouter/another-model'}
            rows={3}
            spellCheck={false}
          />
          <span className="hint">Models that may keep what they are sent. Mail, health and food never go to them.</span>
        </label>
        <p className="hint">Names ending in -free or :free count as free without being listed.</p>
      </section>

      <section className="settings-card" aria-labelledby="models-decider">
        <h3 id="models-decider">Decider</h3>
        <p className="hint">The small model on the processor that answers routines&apos; typed questions: yes or no, one of a list, a score.</p>
        <p>
          {view.decider.configured ? (
            <>
              Started with <code className="mono">{view.decider.command}</code>, answering at <code className="mono">{view.decider.url}</code>.
            </>
          ) : (
            'Not set: routines have no decider.'
          )}
        </p>
        <div className="row">
          <button type="button" className="btn" onClick={() => requestSettingsSection('local')}>
            Set it in Local AI
          </button>
        </div>
      </section>

      {problem && <p className="error">{problem}</p>}
      {save.error && <p className="error">{save.error.message}</p>}
      <div className="row settings-actions">
        <button type="submit" className="btn btn-primary" disabled={!dirty || problem !== null || save.isPending}>
          Save models
        </button>
        <button
          type="button"
          className="btn btn-quiet"
          disabled={!dirty}
          onClick={() => {
            setDraft(settingsOf(view))
            setFreeText(freeModelsText(view.freeModels))
          }}
        >
          Undo changes
        </button>
      </div>
    </form>
  )
}

/** Shiori or the reviewer: an engine, a Claude model, and the OpenCode model that also stands in. */
function TwoEngines(props: {
  name: 'shiori' | 'reviewer'
  choice: EngineChoice
  onChange: (choice: EngineChoice) => void
  lists: ModelLists
  claude: boolean
  claudeHint: string
  openCodeLabel: (claudeChosen: boolean) => string
  openCodeNone: string
  noneLabel?: string
}) {
  const { name, choice, onChange, lists, claude } = props
  const claudeChosen = choice.engine === 'ClaudeCode'
  return (
    <>
      <label className="field">
        <span>Engine</span>
        <select
          className="input"
          value={choice.engine ?? ''}
          onChange={(e) => onChange({ ...choice, engine: (e.target.value || null) as EngineChoice['engine'] })}
        >
          <option value="ClaudeCode" disabled={!claude}>
            {claude ? 'Claude Code' : 'Claude Code (not on this machine)'}
          </option>
          <option value="OpenCode">OpenCode</option>
          {props.noneLabel && <option value="">{props.noneLabel}</option>}
        </select>
        <span className="hint">Works now on: {runsOn(choice, claude, name)}</span>
      </label>
      {claudeChosen && (
        <ModelSelect
          label="Claude Code model"
          engine="ClaudeCode"
          value={choice.claudeModel}
          onChange={(claudeModel) => onChange({ ...choice, claudeModel })}
          lists={lists}
          disabled={!claude}
          hint={props.claudeHint}
        />
      )}
      {choice.engine !== null && (
        <ModelSelect
          label={props.openCodeLabel(claudeChosen)}
          engine="OpenCode"
          value={choice.openCodeModel}
          onChange={(openCodeModel) => onChange({ ...choice, openCodeModel })}
          lists={lists}
          none={claudeChosen ? props.openCodeNone : name === 'reviewer' ? 'Choose a model' : DEFAULT_MODEL.OpenCode}
          hint={claudeChosen ? 'Kept for the times Claude Code is missing or you switch to OpenCode.' : 'Any model OpenCode lists, local ones included.'}
        />
      )}
    </>
  )
}

/** Digests or journals: Claude Code, OpenCode or a local model, and its model. */
function OneEngine({ name, choice, onChange, lists, claude }: { name: string; choice: FunctionChoice; onChange: (c: FunctionChoice) => void; lists: ModelLists; claude: boolean }) {
  const noLocal = choice.engine === 'Local' && (lists.local?.length ?? 0) === 0
  return (
    <>
      <label className="field">
        <span>Engine</span>
        <select className="input" value={choice.engine} onChange={(e) => onChange(withEngine(choice, e.target.value as ModelEngine))}>
          <option value="ClaudeCode" disabled={!claude && choice.engine !== 'ClaudeCode'}>
            {claude ? 'Claude Code' : 'Claude Code (not on this machine)'}
          </option>
          <option value="OpenCode">OpenCode</option>
          <option value="Local">Local model</option>
        </select>
        <span className="hint">
          {choice.engine === 'Local' ? 'Runs on this machine’s graphics card: no quota spent.' : 'Spends the quota of that engine’s account.'}
        </span>
      </label>
      <ModelSelect
        key={`${name}-${choice.engine}`}
        label="Model"
        engine={choice.engine}
        value={choice.model}
        onChange={(model) => onChange({ ...choice, model })}
        lists={lists}
        disabled={choice.engine === 'ClaudeCode' && !claude}
        hint={noLocal ? <LocalLink text="No local model yet: add one in Local AI." /> : undefined}
      />
    </>
  )
}

function LocalLink({ text }: { text: string }) {
  return (
    <>
      {text}{' '}
      <button type="button" className="link-button" onClick={() => requestSettingsSection('local')}>
        Open Local AI
      </button>
    </>
  )
}

/** A model from the engine's list; a saved one nobody lists stays chosen, and is flagged. */
function ModelSelect(props: {
  label: string
  engine: ModelEngine
  value: string | null
  onChange: (model: string | null) => void
  lists: ModelLists
  disabled?: boolean
  none?: string
  hint?: ReactNode
}) {
  const list = listOf(props.engine, props.lists) ?? []
  const listed = props.value === null || list.some((m) => m.id === props.value)
  // Said only when the engine's list is known: a missing engine lists nothing, and that is no fault of the name.
  const unknown = isUnknown(props.value, listOf(props.engine, props.lists))
  return (
    <label className="field">
      <span>{props.label}</span>
      <select className="input" value={props.value ?? ''} disabled={props.disabled} onChange={(e) => props.onChange(e.target.value || null)}>
        <option value="">{props.none ?? DEFAULT_MODEL[props.engine]}</option>
        {!listed && <option value={props.value!}>{unknown ? `${props.value} (not listed now)` : props.value}</option>}
        {list.map((m) => (
          <option key={m.id} value={m.id}>
            {m.name}
          </option>
        ))}
      </select>
      {unknown && (
        <span className="hint">
          {props.engine === 'Local' ? 'Not in Local AI: add it there, or choose another.' : 'Nobody lists it: check the name, or connect that provider in OpenCode.'}
        </span>
      )}
      {props.hint && <span className="hint">{props.hint}</span>}
    </label>
  )
}
