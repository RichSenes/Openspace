import { useEffect, useRef, useState } from 'react'
import type { AgentEngine, EnginesView } from '../api/types'
import { agentNameProblem, modelOptions, type EngineChoice } from './engines'

interface Props {
  value: EngineChoice
  onChange: (value: EngineChoice) => void
  engines: EnginesView | undefined
}

/**
 * Engine, model and, for OpenCode, the agent: the fields of a desk that say what runs it.
 * The options stay short so the selects are never cut; what each choice means is in the
 * hint under its field.
 */
export function EngineFields({ value, onChange, engines }: Props) {
  const claudeMissing = engines !== undefined && !engines.claudeCode
  const models = modelOptions(engines, value.engine, value.model)
  const claude = value.engine === 'ClaudeCode'
  const problem = claude ? undefined : agentNameProblem(value.agentName)
  const agentInput = useRef<HTMLInputElement>(null)
  // A desk on an agent other than build shows it straight away; build is what most use.
  const [agentOpen, setAgentOpen] = useState(value.agentName !== 'build')

  // The browser then refuses to submit the form, with the same words as the hint.
  useEffect(() => {
    agentInput.current?.setCustomValidity(problem ?? '')
  }, [problem])

  return (
    <>
      <label className="field">
        <span>Engine</span>
        <select
          className="input"
          value={value.engine}
          onChange={(e) => onChange({ ...value, engine: e.target.value as AgentEngine, model: '' })}
        >
          <option value="OpenCode">OpenCode</option>
          <option value="ClaudeCode" disabled={claudeMissing && value.engine !== 'ClaudeCode'}>
            Claude Code{claudeMissing ? ' (not found on this machine)' : ''}
          </option>
        </select>
        <span className="hint">
          {claude
            ? 'Uses your Claude account and its limits, shared with everything else you do with it. Reading is always allowed; editing, commands and the web follow the role.'
            : 'Runs the models of your OpenCode server.'}
        </span>
      </label>
      <label className="field">
        <span>Model</span>
        <select className="input" value={value.model} onChange={(e) => onChange({ ...value, model: e.target.value })}>
          <option value="">Default</option>
          {models.map((m) => (
            <option key={m.id} value={m.id}>
              {m.name}
            </option>
          ))}
        </select>
        <span className="hint">{claude ? "Default is Claude Code's own model." : "Default is the role's model, or else the agent's own."}</span>
      </label>
      {!claude && (
        <details className="advanced" open={agentOpen || problem !== undefined} onToggle={(e) => setAgentOpen(e.currentTarget.open)}>
          <summary className="hint">Advanced: OpenCode agent</summary>
          <label className="field">
            <span>OpenCode agent that runs the desk</span>
            <input
              ref={agentInput}
              className="input mono"
              value={value.agentName}
              onChange={(e) => onChange({ ...value, agentName: e.target.value })}
              aria-invalid={problem !== undefined}
              maxLength={64}
              required
            />
            {problem ? (
              <span className="error">{problem}</span>
            ) : (
              <span className="hint">build works on the files, plan only reads. Lowercase letters, digits and dashes only.</span>
            )}
          </label>
        </details>
      )}
    </>
  )
}
