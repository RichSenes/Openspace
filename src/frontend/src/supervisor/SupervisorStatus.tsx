import { Sparkles } from 'lucide-react'
import { useSupervisorJobs } from '../api/hooks'

/** In the status bar while the supervisor plans: its plans run in the background. */
export function SupervisorStatus({ onOpen }: { onOpen: () => void }) {
  const planning = (useSupervisorJobs().data ?? []).filter((j) => j.state === 'Planning')
  if (planning.length === 0) return null
  const title = planning.map((j) => j.what).join('\n')
  return (
    <button type="button" className="claude-meter" onClick={onOpen} title={title}>
      <Sparkles size={14} aria-hidden />
      <span>{planning.length === 1 ? `Supervisor planning: ${planning[0].what}` : `Supervisor planning ${planning.length} requests`}</span>
    </button>
  )
}
