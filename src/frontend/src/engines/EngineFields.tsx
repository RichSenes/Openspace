import type { AgentEngine, EnginesView } from '../api/types'
import { modelOptions, type EngineChoice } from './engines'

interface Props {
  value: EngineChoice
  onChange: (value: EngineChoice) => void
  engines: EnginesView | undefined
  /** What "default" means for the model: the role's, or the engine's own. */
  defaultLabel?: string
}

/** Engine, model and, for OpenCode, the agent: the fields of a desk that say what runs it. */
export function EngineFields({ value, onChange, engines, defaultLabel }: Props) {
  const claudeMissing = engines !== undefined && !engines.claudeCode
  const models = modelOptions(engines, value.engine, value.model)
  return (
    <>
      <label className="field">
        <span>Engine</span>
        <select
          className="input"
          value={value.engine}
          onChange={(e) => onChange({ ...value, engine: e.target.value as AgentEngine, model: '' })}
        >
          <option value="OpenCode">OpenCode: the models of your OpenCode server</option>
          <option value="ClaudeCode" disabled={claudeMissing && value.engine !== 'ClaudeCode'}>
            Claude Code: your Claude account{claudeMissing ? ' (not found on this machine)' : ''}
          </option>
        </select>
      </label>
      <label className="field">
        <span>Model</span>
        <select className="input" value={value.model} onChange={(e) => onChange({ ...value, model: e.target.value })}>
          <option value="">{defaultLabel ?? (value.engine === 'ClaudeCode' ? "Claude Code's default" : "The role's, or the agent's default")}</option>
          {models.map((m) => (
            <option key={m.id} value={m.id}>
              {m.name}
            </option>
          ))}
        </select>
      </label>
      {value.engine === 'ClaudeCode' ? (
        <p className="hint">
          Uses your Claude account and its limits, shared with everything else you do with it. Reading is always allowed; editing, commands and the web follow the role.
        </p>
      ) : (
        <details className="advanced">
          <summary className="hint">Advanced: OpenCode agent</summary>
          <label className="field">
            <span>OpenCode agent that runs the desk (build works, plan only reads)</span>
            <input className="input mono" value={value.agentName} onChange={(e) => onChange({ ...value, agentName: e.target.value })} required />
          </label>
        </details>
      )}
    </>
  )
}
