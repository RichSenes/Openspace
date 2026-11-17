import { TriangleAlert } from 'lucide-react'
import { useEngines, useOffices } from '../api/hooks'
import { setupProblems } from './setup'

/** In the top bar while an engine the desks need is missing: it opens Agents, where the engines are. */
export function SetupCheck({ onOpen }: { onOpen: () => void }) {
  const problems = setupProblems(useEngines().data, useOffices().data ?? [])
  return (
    <>
      {problems.map((p) => (
        <button key={p.key} type="button" className="claude-meter claude-meter-limited" onClick={onOpen} title={p.title}>
          <TriangleAlert size={14} aria-hidden />
          <span>{p.text}</span>
        </button>
      ))}
    </>
  )
}
