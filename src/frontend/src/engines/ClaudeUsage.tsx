import { RefreshCw, TriangleAlert } from 'lucide-react'
import { useCheckClaudeUsage, useEngines } from '../api/hooks'
import type { ClaudeUsage, UsageWindow } from '../api/types'
import './engines.css'

const when = (iso: string | null) =>
  iso ? new Date(iso).toLocaleString(undefined, { weekday: 'short', hour: '2-digit', minute: '2-digit' }) : null

const percent = (window: UsageWindow | null) => (window ? Math.round(window.used * 100) : null)

/** A window of the Claude account's limits: a bar, and the numbers in words beside it. */
function Meter({ label, window }: { label: string; window: UsageWindow | null }) {
  if (!window) return null
  const used = percent(window)!
  const reset = when(window.resetsAt)
  return (
    <div className="usage">
      <span>
        {label}: <strong>{used}%</strong> used, <strong>{Math.max(0, 100 - used)}%</strong> left
        {reset && <span className="hint"> · resets {reset}</span>}
      </span>
      <div className="usage-bar" role="meter" aria-label={`${label} used`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={used}>
        <div style={{ width: `${Math.min(100, used)}%` }} />
      </div>
    </div>
  )
}

/** Both windows of the Claude account, with when they were last read and a way to read them now. */
export function ClaudeUsageMeters() {
  const engines = useEngines()
  const check = useCheckClaudeUsage()
  const usage = engines.data?.claudeUsage
  if (!engines.data?.claudeCode) return <p className="hint">Claude Code is not on this machine: install it and sign in to see your account here.</p>
  return (
    <div className="claude-usage">
      {usage ? (
        <div className="usages">
          <Meter label="Next 5 hours" window={usage.fiveHours} />
          <Meter label="This week" window={usage.week} />
        </div>
      ) : (
        <p className="hint">Not read yet: it shows up after an agent works on Claude Code, or when you check now.</p>
      )}
      <div className="row">
        <button type="button" className="btn" disabled={check.isPending} onClick={() => check.mutate()}>
          <RefreshCw size={16} aria-hidden /> {check.isPending ? 'Checking…' : 'Check now'}
        </button>
        <span className="hint">
          {usage ? `Read ${when(usage.seenAt)}. ` : ''}Checking asks Haiku for one word, a few tokens of your account.
        </span>
      </div>
      {check.error && <p className="error">{check.error.message}</p>}
      <p className="hint">Claude tells how much of each window is used, not a count of tokens: the bars are what is left of your plan.</p>
    </div>
  )
}

/** One line for the top bar: how much of the Claude account is left; it opens the settings. */
export function ClaudeMeter({ onOpen }: { onOpen: () => void }) {
  const engines = useEngines().data
  const usage: ClaudeUsage | null | undefined = engines?.claudeUsage
  const paused = engines?.claudePausedUntil
  if (!usage && !paused) return null
  const fiveHours = percent(usage?.fiveHours ?? null)
  const week = percent(usage?.week ?? null)
  const limited = Boolean(paused) || (usage !== undefined && usage !== null && usage.status !== 'allowed')
  return (
    <button type="button" className={limited ? 'claude-meter claude-meter-limited' : 'claude-meter'} onClick={onOpen} title="Your Claude account: open Settings">
      {limited && <TriangleAlert size={14} aria-hidden />}
      <span>
        Claude{paused ? `: desks paused until ${clock(paused)}` : limited ? ': limit reached' : ''}
        {fiveHours !== null && ` · 5h ${100 - fiveHours}% left`}
        {week !== null && ` · week ${100 - week}% left`}
      </span>
    </button>
  )
}

const clock = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
