import type { LinkState } from '../realtime/useBuildingHub'
import './layout.css'

export type View = 'building' | 'board' | 'rooms' | 'reports' | 'agents'

const VIEWS: { id: View; label: string }[] = [
  { id: 'building', label: 'Building' },
  { id: 'board', label: 'Board' },
  { id: 'rooms', label: 'Rooms' },
  { id: 'reports', label: 'Reports' },
  { id: 'agents', label: 'Agents' },
]

const LINK_LABEL: Record<LinkState, string> = {
  connecting: 'Connecting',
  live: 'Live',
  offline: 'Offline',
}

interface Props {
  view: View
  onView: (view: View) => void
  link: LinkState
}

export function TopBar({ view, onView, link }: Props) {
  return (
    <header className="topbar">
      <span className="wordmark pixel">Openspace</span>
      <nav className="tabs" aria-label="Views">
        {VIEWS.map((v) => (
          <button
            key={v.id}
            type="button"
            className="tab"
            aria-current={view === v.id ? 'page' : undefined}
            onClick={() => onView(v.id)}
          >
            {v.label}
          </button>
        ))}
      </nav>
      <span className={`link link-${link}`} title="Connection to the Openspace server">
        {LINK_LABEL[link]}
      </span>
    </header>
  )
}
