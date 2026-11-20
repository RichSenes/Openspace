import { TriangleAlert } from 'lucide-react'
import { useEngines, useOffices } from '../api/hooks'
import { requestSettingsSection } from '../settings/sections'
import { setupProblems } from './setup'

/** In the top bar while an engine the desks need is missing: it opens Settings › Engines. */
export function SetupCheck({ onOpen }: { onOpen: () => void }) {
  const problems = setupProblems(useEngines().data, useOffices().data ?? [])
  return (
    <>
      {problems.map((p) => (
        <button
          key={p.key}
          type="button"
          className="claude-meter claude-meter-limited"
          title={p.title}
          onClick={() => {
            requestSettingsSection('engines')
            onOpen()
          }}
        >
          <TriangleAlert size={14} aria-hidden />
          <span>{p.text}</span>
        </button>
      ))}
    </>
  )
}
