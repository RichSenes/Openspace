import { useCallback, useLayoutEffect, useRef, useState, type KeyboardEvent as KeyEvent } from 'react'
import { ChevronDown } from 'lucide-react'
import { SupervisorStatus } from '../supervisor/SupervisorStatus'
import { SetupCheck } from '../setup/SetupCheck'
import { NeedsYou } from '../activity/NeedsYou'
import { ReleaseBanner } from '../release/ReleaseBanner'
import { MuteButton } from '../sounds/MuteButton'
import { ClaudeMeter, GpuStatus, LimitPauses } from '../engines/ClaudeUsage'
import type { LinkState } from '../realtime/useBuildingHub'
import type { NeedsYouTarget } from '../activity/needs'
import { useDismiss } from '../ui/useDismiss'
import { fitTabs, type TabSplit } from './tabFit'
import './layout.css'

export type View = 'building' | 'shiori' | 'board' | 'rooms' | 'routines' | 'reports' | 'security' | 'office' | 'agents' | 'settings'

const VIEWS: { id: View; label: string }[] = [
  { id: 'building', label: 'Building' },
  { id: 'shiori', label: 'Shiori' },
  { id: 'board', label: 'Board' },
  { id: 'rooms', label: 'Rooms' },
  { id: 'routines', label: 'Routines' },
  { id: 'reports', label: 'Reports' },
  { id: 'security', label: 'Security' },
  { id: 'office', label: 'Office' },
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
        <Tabs view={view} onView={onView} />
        <div className="topbar-end">
          <NeedsYou onOpen={onOpen} />
          <MuteButton />
          <span className={`link link-${link}`} title={`Connection to the Openspace server: ${LINK_LABEL[link]}`}>
            <span className="wide-only">{LINK_LABEL[link]}</span>
          </span>
        </div>
      </header>
      <div className="statusbar" aria-label="Engines, accounts and graphics card">
        <SetupCheck onOpen={() => onView('settings')} />
        <LimitPauses onOpen={() => onView('board')} />
        <GpuStatus onOpen={() => onView('board')} />
        <SupervisorStatus onOpen={() => onView('board')} />
        <ClaudeMeter onOpen={() => onView('settings')} />
      </div>
      <ReleaseBanner />
    </div>
  )
}

/**
 * The views as tabs, as many as the bar has room for; the others behind a More button, never
 * cut off. The current view always keeps its tab. The widths come from a hidden copy of every
 * tab, so a tab that went behind More is still measured when the window grows again.
 */
function Tabs({ view, onView }: { view: View; onView: (view: View) => void }) {
  const nav = useRef<HTMLElement>(null)
  const measure = useRef<HTMLDivElement>(null)
  const [split, setSplit] = useState<TabSplit>({ shown: VIEWS.map((_, i) => i), more: [] })
  const current = VIEWS.findIndex((v) => v.id === view)

  useLayoutEffect(() => {
    const bar = nav.current
    const copies = measure.current
    if (!bar || !copies) return
    const fit = () => {
      const gap = parseFloat(getComputedStyle(bar).columnGap) || 0
      const widths = [...copies.children].map((c) => (c as HTMLElement).offsetWidth + gap)
      const more = widths.pop() ?? 0
      const next = fitTabs(widths, bar.clientWidth + gap, more, current)
      setSplit((was) => (sameSplit(was, next) ? was : next))
    }
    fit()
    const watch = new ResizeObserver(fit)
    watch.observe(bar)
    // The copies change size once the fonts have loaded.
    watch.observe(copies)
    return () => watch.disconnect()
  }, [current])

  return (
    <nav className="tabs" aria-label="Views" ref={nav}>
      {split.shown.map((i) => (
        <Tab key={VIEWS[i].id} label={VIEWS[i].label} current={view === VIEWS[i].id} onClick={() => onView(VIEWS[i].id)} />
      ))}
      {split.more.length > 0 && <MoreTabs views={split.more.map((i) => VIEWS[i])} onView={onView} />}
      <div className="tabs-measure" ref={measure} aria-hidden>
        {VIEWS.map((v) => (
          <span key={v.id} className="tab">
            {v.label}
          </span>
        ))}
        <span className="tab">
          More <ChevronDown size={14} />
        </span>
      </div>
    </nav>
  )
}

const sameSplit = (a: TabSplit, b: TabSplit) => a.shown.join() === b.shown.join() && a.more.join() === b.more.join()

function Tab({ label, current, onClick }: { label: string; current: boolean; onClick: () => void }) {
  return (
    <button type="button" className="tab" aria-current={current ? 'page' : undefined} onClick={onClick}>
      {label}
    </button>
  )
}

/** The views that did not fit, in a list under the More button. Arrows move between them. */
function MoreTabs({ views, onView }: { views: { id: View; label: string }[]; onView: (view: View) => void }) {
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const list = useRef<HTMLUListElement>(null)
  const close = useCallback(() => setOpen(false), [])
  useDismiss(open, close, box, button)

  // Opening moves the focus to the first view, so the arrows and Enter work at once.
  useLayoutEffect(() => {
    if (open) list.current?.querySelector('button')?.focus()
  }, [open])

  const onKey = (e: KeyEvent) => {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
    e.preventDefault()
    const items = [...(list.current?.querySelectorAll('button') ?? [])]
    const at = items.indexOf(document.activeElement as HTMLButtonElement)
    const step = e.key === 'ArrowDown' ? 1 : -1
    items[(at + step + items.length) % items.length]?.focus()
  }

  return (
    <div className="tabs-more" ref={box}>
      <button
        ref={button}
        type="button"
        className="tab"
        aria-expanded={open}
        aria-controls="more-views"
        onClick={() => setOpen(!open)}
      >
        More <ChevronDown size={14} aria-hidden />
      </button>
      {open && (
        <ul id="more-views" className="tabs-menu" ref={list} onKeyDown={onKey}>
          {views.map((v) => (
            <li key={v.id}>
              <button
                type="button"
                onClick={() => {
                  setOpen(false)
                  onView(v.id)
                }}
              >
                {v.label}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
