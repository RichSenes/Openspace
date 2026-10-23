import { useEngines } from '../api/hooks'
import type { UsageWindow } from '../api/types'
import { ENGINE_LABEL } from './engines'
import './engines.css'

const when = (iso: string | null) =>
  iso ? new Date(iso).toLocaleString(undefined, { weekday: 'short', hour: '2-digit', minute: '2-digit' }) : null

/** A window of the Claude account's limits: a bar, and the numbers in words beside it. */
function Usage({ label, window }: { label: string; window: UsageWindow | null }) {
  if (!window) return null
  const percent = Math.round(window.used * 100)
  const reset = when(window.resetsAt)
  return (
    <div className="usage">
      <span>
        {label}: <strong>{percent}%</strong> used{reset && <span className="hint"> · resets {reset}</span>}
      </span>
      <div className="usage-bar" role="meter" aria-label={`${label} used`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}>
        <div style={{ width: `${Math.min(100, percent)}%` }} />
      </div>
    </div>
  )
}

/** What can run agents here: OpenCode and Claude Code, who supervises and reviews, how much of the Claude account is used. */
export function EnginesCard() {
  const engines = useEngines()
  const e = engines.data
  if (!e) return null
  return (
    <section className="engines-card" aria-label="Engines">
      <ul>
        <li>
          <strong>OpenCode</strong> {e.openCode ? `· ${e.openCodeModels.length} models` : '· not reachable: start opencode serve'}
        </li>
        <li>
          <strong>Claude Code</strong> {e.claudeCode ? '· ready, with your Claude account' : '· not found on this machine'}
        </li>
        <li>
          Plans and drafts: <strong>{ENGINE_LABEL[e.supervisor]}</strong> · Review before yours: <strong>{e.reviewer ? ENGINE_LABEL[e.reviewer] : 'nobody'}</strong>
        </li>
      </ul>
      {e.claudeUsage ? (
        <div className="usages">
          <Usage label="Claude, 5 hours" window={e.claudeUsage.fiveHours} />
          <Usage label="Claude, week" window={e.claudeUsage.week} />
        </div>
      ) : (
        e.claudeCode && <p className="hint">The Claude account&apos;s usage shows up after the first agent runs on Claude Code.</p>
      )}
    </section>
  )
}
