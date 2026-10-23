import { useEngines } from '../api/hooks'
import { ClaudeUsageMeters } from './ClaudeUsage'
import { ENGINE_LABEL } from './engines'
import './engines.css'

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
      {e.claudeCode && <ClaudeUsageMeters />}
    </section>
  )
}
