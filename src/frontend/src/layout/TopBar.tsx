import { SupervisorStatus } from '../supervisor/SupervisorStatus'
import { SetupCheck } from '../setup/SetupCheck'
import { NeedsYou } from '../activity/NeedsYou'
import { ReleaseBanner } from '../release/ReleaseBanner'
import { MuteButton } from '../sounds/MuteButton'
import { ClaudeMeter, GpuStatus, LimitPauses } from '../engines/ClaudeUsage'
import type { LinkState } from '../realtime/useBuildingHub'
import type { NeedsYouTarget } from '../activity/needs'
import './layout.css'

export type View = 'building' | 'board' | 'rooms' | 'routines' | 'reports' | 'security' | 'agents' | 'settings'

const VIEWS: { id: View; label: string }[] = [
  { id: 'building', label: 'Building' },
  { id: 'board', label: 'Board' },
  { id: 'rooms', label: 'Rooms' },
  { id: 'routines', label: 'Routines' },
  { id: 'reports', label: 'Reports' },
  { id: 'security', label: 'Security' },
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
  /** Opens what waits for the person where it is decided: its office, its view, its task. */
  onOpen: (target: NeedsYouTarget) => void
}

/**
 * Navigation on one line, and under it a strip of what the accounts and the graphics card
 * are up to. The strip shows only when one of them has something to say, and wraps on
 * narrow windows instead of pushing the views out of sight.
 */
export function TopBar({ view, onView, link, onOpen }: Props) {
  return (
    <div className="top">
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
          <NeedsYou onOpen={onOpen} />
          <MuteButton />
          <span className={`link link-${link}`} title={`Connection to the Openspace server: ${LINK_LABEL[link]}`}>
            <span className="wide-only">{LINK_LABEL[link]}</span>
          </span>
        </div>
      </header>
      <div className="statusbar" aria-label="Engines, accounts and graphics card">
        <SetupCheck onOpen={() => onView('agents')} />
        <LimitPauses onOpen={() => onView('board')} />
        <GpuStatus onOpen={() => onView('board')} />
        <SupervisorStatus onOpen={() => onView('board')} />
        <ClaudeMeter onOpen={() => onView('settings')} />
      </div>
      <ReleaseBanner />
    </div>
  )
}
