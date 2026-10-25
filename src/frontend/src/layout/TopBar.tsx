import { NeedsYou } from '../activity/NeedsYou'
import { MuteButton } from '../sounds/MuteButton'
import { ClaudeMeter, LimitPauses } from '../engines/ClaudeUsage'
import type { LinkState } from '../realtime/useBuildingHub'
import './layout.css'

export type View = 'building' | 'board' | 'rooms' | 'reports' | 'agents' | 'settings'

const VIEWS: { id: View; label: string }[] = [
  { id: 'building', label: 'Building' },
  { id: 'board', label: 'Board' },
  { id: 'rooms', label: 'Rooms' },
  { id: 'reports', label: 'Reports' },
  { id: 'agents', label: 'Agents' },
  { id: 'settings', label: 'Settings' },
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
      <div className="topbar-end">
        <NeedsYou />
        <LimitPauses onOpen={() => onView('board')} />
        <ClaudeMeter onOpen={() => onView('settings')} />
        <MuteButton />
        <span className={`link link-${link}`} title="Connection to the Openspace server">
          {LINK_LABEL[link]}
        </span>
      </div>
    </header>
  )
}
