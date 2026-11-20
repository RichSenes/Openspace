import { useState, type FormEvent } from 'react'
import { CircleCheck, Plus, Search, TriangleAlert, X } from 'lucide-react'
import { useCheckLocalModel, useGpu, useLocalAi, useSaveLocalAi } from '../api/hooks'
import type { ModelCheck } from '../api/types'
import { gpuBadge } from '../engines/gpu'
import { useToast } from '../ui/toastContext'
import { cardLine, changed, draftOf, modelOf, newModel, settingsOf, type LocalAiDraft, type ModelDraft } from './localAi'

/**
 * Models served on this machine's graphics card, the programs that come before them and the
 * small model on the processor that answers routines' questions. One form, one save.
 */
export function LocalAiSection() {
  const local = useLocalAi()
  if (local.error) return <p className="error">Could not read the local models: {local.error.message}</p>
  if (!local.data) return <p className="hint">Reading the local models…</p>
  return <LocalAiForm key={JSON.stringify(local.data.settings)} view={local.data} />
}

function LocalAiForm({ view }: { view: NonNullable<ReturnType<typeof useLocalAi>['data']> }) {
  const [draft, setDraft] = useState<LocalAiDraft>(() => draftOf(view.settings))
  const save = useSaveLocalAi()
  const gpu = useGpu().data
  const toast = useToast()
  const set = (change: Partial<LocalAiDraft>) => setDraft((d) => ({ ...d, ...change }))
  const setModel = (key: string, change: Partial<ModelDraft>) =>
    setDraft((d) => ({ ...d, models: d.models.map((m) => (m.key === key ? { ...m, ...change } : m)) }))
  const dirty = changed(view.settings, draft)
  const badge = gpuBadge(gpu)
  const memory = cardLine(view.cardMiB, view.usedMiB)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate(settingsOf(draft), { onSuccess: () => toast('Local AI saved: it applies now') })
  }

  return (
    <form className="settings-form local-ai" onSubmit={submit}>
      <section className="settings-card" aria-labelledby="local-on">
        <h3 id="local-on">On this machine</h3>
        <label className="setting-choice">
          <input type="checkbox" checked={draft.enabled} onChange={(e) => set({ enabled: e.target.checked })} />
          <span>
            <strong>Desks and routines may use models on this machine</strong>
            <span className="hint">
              Openspace starts a model on the graphics card when a desk or a routine needs it, one at a time, and unloads it when nobody does.
              Desks name them as local/&lt;name&gt;.
            </span>
          </span>
        </label>
        <p>
          {badge ? badge.text : draft.enabled ? 'The card is free: no model is loaded.' : 'Off: no model is started.'}
          {memory && <span className="hint"> · {memory}</span>}
        </p>
        <label className="field">
          <span>Where the model server answers</span>
          <input className="input mono" value={draft.serverUrl} onChange={(e) => set({ serverUrl: e.target.value })} required />
          <span className="hint">The address the command below opens: an OpenAI-compatible API, such as llama-server&apos;s.</span>
        </label>
        <div className="local-numbers">
          <label className="field">
            <span>Unload after (minutes)</span>
            <input
              className="input"
              type="number"
              min={1}
              max={1440}
              value={draft.idleUnloadMinutes}
              onChange={(e) => set({ idleUnloadMinutes: Number(e.target.value) })}
              required
            />
            <span className="hint">Of nobody using it, to give the card back.</span>
          </label>
          <label className="field">
            <span>A task keeps the card (minutes)</span>
            <input
              className="input"
              type="number"
              min={1}
              max={1440}
              value={draft.yieldGraceMinutes}
              onChange={(e) => set({ yieldGraceMinutes: Number(e.target.value) })}
              required
            />
            <span className="hint">After a program below asks for it, before the task goes back to the queue.</span>
          </label>
        </div>
      </section>

      <section className="settings-card local-models" aria-labelledby="local-models">
        <h3 id="local-models">Models</h3>
        {draft.models.length === 0 && <p className="hint">None yet: add the first one, with the command that starts its server.</p>}
        {draft.models.map((m) => (
          <ModelCard
            key={m.key}
            model={m}
            onChange={(change) => setModel(m.key, change)}
            onRemove={() => set({ models: draft.models.filter((x) => x.key !== m.key) })}
          />
        ))}
        <div className="row">
          <button type="button" className="btn" onClick={() => set({ models: [...draft.models, newModel(draft.models.map((m) => m.id))] })}>
            <Plus size={16} aria-hidden /> Add a model
          </button>
        </div>
      </section>

      <section className="settings-card" aria-labelledby="local-holders">
        <h3 id="local-holders">Programs that come first</h3>
        <p className="hint">
          While one of these runs, the card is theirs: a local task finishes or goes back to the queue, then the model is unloaded. Openspace
          finds them by words in their command line; it never looks at what they do.
        </p>
        {draft.holders.length > 0 && (
          <ul className="local-holders">
            {draft.holders.map((h, i) => (
              <li key={i}>
                <label className="field">
                  <span>Name</span>
                  <input
                    className="input"
                    value={h.name}
                    onChange={(e) => set({ holders: draft.holders.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) })}
                    placeholder="e.g. Image studio"
                  />
                </label>
                <label className="field">
                  <span>Found by</span>
                  <input
                    className="input mono"
                    value={h.match}
                    onChange={(e) => set({ holders: draft.holders.map((x, j) => (j === i ? { ...x, match: e.target.value } : x)) })}
                    placeholder="e.g. main.py --listen"
                  />
                </label>
                <span className="local-holder-state hint">{view.holdersRunning.includes(h.name) ? 'Running now' : ''}</span>
                <button type="button" className="btn btn-quiet" onClick={() => set({ holders: draft.holders.filter((_, j) => j !== i) })} aria-label={`Remove ${h.name || 'this program'}`}>
                  <X size={16} aria-hidden />
                </button>
              </li>
            ))}
          </ul>
        )}
        <div className="row">
          <button type="button" className="btn" onClick={() => set({ holders: [...draft.holders, { name: '', match: '' }] })}>
            <Plus size={16} aria-hidden /> Add a program
          </button>
        </div>
      </section>

      <section className="settings-card" aria-labelledby="local-decider">
        <h3 id="local-decider">Small model for routines&apos; questions</h3>
        <p className="hint">
          A model on the processor that answers routines yes or no, picks from a list or gives a score. It needs no graphics card. Leave the
          command empty for none.
        </p>
        <label className="field">
          <span>Command</span>
          <input
            className="input mono"
            value={draft.decider.command}
            onChange={(e) => set({ decider: { ...draft.decider, command: e.target.value } })}
            placeholder="/opt/llm/decider.sh"
          />
        </label>
        <label className="field">
          <span>Arguments</span>
          <textarea className="input mono" value={draft.decider.arguments} onChange={(e) => set({ decider: { ...draft.decider, arguments: e.target.value } })} />
          <span className="hint">One per line.</span>
        </label>
        <label className="field">
          <span>Where it answers</span>
          <input className="input mono" value={draft.decider.url} onChange={(e) => set({ decider: { ...draft.decider, url: e.target.value } })} required />
        </label>
      </section>

      {save.error && <p className="error">{save.error.message}</p>}
      <div className="row settings-actions">
        <button type="submit" className="btn btn-primary" disabled={!dirty || save.isPending}>
          {save.isPending ? 'Saving…' : 'Save local AI'}
        </button>
        <button type="button" className="btn btn-quiet" disabled={!dirty} onClick={() => setDraft(draftOf(view.settings))}>
          Undo changes
        </button>
      </div>
      <p className="hint settings-actions">OpenCode sees new or renamed models after it restarts.</p>
    </form>
  )
}

function ModelCard({ model, onChange, onRemove }: { model: ModelDraft; onChange: (change: Partial<ModelDraft>) => void; onRemove: () => void }) {
  const check = useCheckLocalModel()
  const [result, setResult] = useState<ModelCheck>()
  return (
    <article className="local-model" aria-label={model.name || model.id}>
      <div className="local-model-head">
        <strong>{model.name || model.id}</strong>
        <span className="hint mono">local/{model.id}</span>
        <button type="button" className="btn btn-quiet" onClick={onRemove} aria-label={`Remove ${model.id}`}>
          <X size={16} aria-hidden /> Remove
        </button>
      </div>
      <div className="local-model-grid">
        <label className="field">
          <span>Name for desks</span>
          <input className="input mono" value={model.id} onChange={(e) => onChange({ id: e.target.value })} required />
          <span className="hint">Lowercase letters, digits, dots and dashes.</span>
        </label>
        <label className="field">
          <span>Name to show</span>
          <input className="input" value={model.name} onChange={(e) => onChange({ name: e.target.value })} placeholder="e.g. Gemma 4 26B" />
        </label>
      </div>
      <label className="field">
        <span>Command that starts its server</span>
        <input className="input mono" value={model.command} onChange={(e) => onChange({ command: e.target.value })} placeholder="/opt/llm/start-gemma.sh or llama-server" required />
        <span className="hint">It must keep running in the foreground; Openspace stops it to unload the model.</span>
      </label>
      <label className="field">
        <span>Arguments</span>
        <textarea className="input mono" value={model.arguments} onChange={(e) => onChange({ arguments: e.target.value })} placeholder={'-m\n/models/gemma.gguf\n--port\n8080'} />
        <span className="hint">One per line, as the command takes them.</span>
      </label>
      <div className="local-model-grid">
        <label className="field">
          <span>Graphics memory it needs (MiB)</span>
          <input className="input" type="number" min={0} value={model.memoryMiB} onChange={(e) => onChange({ memoryMiB: Number(e.target.value) })} />
          <span className="hint">It is not started while less is free. 0 starts it anyway.</span>
        </label>
        <label className="field">
          <span>Context (tokens)</span>
          <input className="input" type="number" min={512} value={model.context} onChange={(e) => onChange({ context: Number(e.target.value) })} />
          <span className="hint">How much conversation it holds, as its server is set.</span>
        </label>
      </div>
      <div className="row">
        <button type="button" className="btn" disabled={check.isPending} onClick={() => check.mutate(modelOf(model), { onSuccess: setResult })}>
          <Search size={16} aria-hidden /> {check.isPending ? 'Checking…' : 'Check it'}
        </button>
      </div>
      <p className="hint">Looks at the command, its files and the card&apos;s memory, without loading the model.</p>
      {check.error && <p className="error">{check.error.message}</p>}
      {result && (
        <ul className="local-check" aria-label={`Check of ${model.id}`}>
          {result.problems.map((p) => (
            <li key={p} className="error">
              <TriangleAlert size={14} aria-hidden /> {p}
            </li>
          ))}
          {result.fine.map((f) => (
            <li key={f}>
              <CircleCheck size={14} aria-hidden /> {f}
            </li>
          ))}
        </ul>
      )}
    </article>
  )
}
