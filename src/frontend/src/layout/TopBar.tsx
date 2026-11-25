import { useCallback, useLayoutEffect, useRef, useState, type KeyboardEvent as KeyEvent } from 'react'
import { ChevronDown, LayoutGrid } from 'lucide-react'
import { SupervisorStatus } from '../supervisor/SupervisorStatus'
import { SetupCheck } from '../setup/SetupCheck'
import { NeedsYou } from '../activity/NeedsYou'
import { ReleaseBanner } from '../release/ReleaseBanner'
import { MuteButton } from '../sounds/MuteButton'
import { ClaudeMeter, GpuStatus, LimitPauses } from '../engines/ClaudeUsage'
import type { LinkState } from '../realtime/useBuildingHub'
import type { NeedsYouTarget } from '../activity/needs'
import { useDismiss } from '../ui/useDismiss'
import { GROUPS, pageInfo, type View } from './pageList'
import './layout.css'

export type { View } from './pageList'

const LINK_LABEL: Record<LinkState, string> = {
  connecting: 'Connecting',
  live: 'Live',
  offline: 'Offline',
}

interface Props {
  /** The page open over the world, or none. */
  page: View | null
  onPage: (view: View) => void
  /** Back to the world: closes the page. */
  onHome: () => void
  link: LinkState
  /** Opens what waits for the person where it is decided: its office, its view, its task. */
  onOpen: (target: NeedsYouTarget) => void
}

/**
 * One thin line over the world: the name (back to the world), the menu of pages, what the
 * accounts and the graphics card are up to when they have something to say, what waits for
 * the person, the sound and the connection. On narrow windows the status goes to a second line.
 */
export function TopBar({ page, onPage, onHome, link, onOpen }: Props) {
  // The bar's whole height, shared with what sits under it: one line, or two when the status wraps.
  const top = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const bar = top.current?.querySelector<HTMLElement>('.topbar')
    const shell = top.current?.parentElement
    if (!bar || !shell) return
    const set = () => shell.style.setProperty('--bar-h', `${bar.getBoundingClientRect().height}px`)
    set()
    const watch = new ResizeObserver(set)
    watch.observe(bar)
    return () => watch.disconnect()
  }, [])
  return (
    <div className="top" ref={top}>
      <header className="topbar">
        <button type="button" className="wordmark pixel" onClick={onHome} aria-label="Openspace: back to the lab" title="Back to the lab">
          Openspace
        </button>
        <PagesMenu page={page} onPage={onPage} />
        <div className="statusbar" aria-label="Engines, accounts and graphics card">
          <SetupCheck onOpen={() => onPage('settings')} />
          <LimitPauses onOpen={() => onPage('board')} />
          <GpuStatus onOpen={() => onPage('board')} />
          <SupervisorStatus onOpen={() => onPage('board')} />
          <ClaudeMeter onOpen={() => onPage('settings')} />
        </div>
        <div className="topbar-end">
          <NeedsYou onOpen={onOpen} />
          <MuteButton />
          <span className={`link link-${link}`} role="status" title={`Connection to the Openspace server: ${LINK_LABEL[link]}`}>
            <span className="sr-only">{LINK_LABEL[link]}</span>
          </span>
        </div>
      </header>
      <ReleaseBanner />
    </div>
  )
}

/** Every page, in groups, under one button; arrows move between them, Escape closes. */
function PagesMenu({ page, onPage }: { page: View | null; onPage: (view: View) => void }) {
  const [open, setOpen] = useState(false)
  // Opened from the keyboard, the focus goes into the menu; from the pointer it stays put.
  const [byKey, setByKey] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const panel = useRef<HTMLDivElement>(null)
  const close = useCallback(() => setOpen(false), [])
  useDismiss(open, close, box, button)
  const current = page ? pageInfo(page) : null
  const Icon = current?.icon ?? LayoutGrid

  // Opening moves the focus to the current page (or the first), so the arrows and Enter work at once.
  useLayoutEffect(() => {
    if (!open || !byKey) return
    const items = panel.current?.querySelectorAll<HTMLButtonElement>('button') ?? []
    ;([...items].find((b) => b.getAttribute('aria-current') === 'page') ?? items[0])?.focus()
  }, [open, byKey])

  const onKey = (e: KeyEvent) => {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(e.key)) return
    e.preventDefault()
    const items = [...(panel.current?.querySelectorAll('button') ?? [])]
    const at = items.indexOf(document.activeElement as HTMLButtonElement)
    const next = e.key === 'Home' ? 0 : e.key === 'End' ? items.length - 1 : (at + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length
    items[next]?.focus()
  }

  return (
    <div className="pages" ref={box}>
      <button
        ref={button}
        type="button"
        className="pages-button"
        aria-expanded={open}
        aria-controls="pages-menu"
        onClick={(e) => {
          setByKey(e.detail === 0)
          setOpen(!open)
        }}
      >
        <Icon size={16} aria-hidden />
        <span>{current?.label ?? 'Pages'}</span>
        <ChevronDown size={14} aria-hidden className="pages-chevron" />
      </button>
      {open && (
        <div id="pages-menu" className="pages-menu" ref={panel} onKeyDown={onKey}>
          {GROUPS.map((g) => (
            <section key={g.label} className="pages-group" aria-labelledby={`pages-${g.label}`}>
              <h2 id={`pages-${g.label}`}>{g.label}</h2>
              <ul>
                {g.pages.map((p) => (
                  <li key={p.id}>
                    <button
                      type="button"
                      aria-current={p.id === page ? 'page' : undefined}
                      onClick={() => {
                        setOpen(false)
                        onPage(p.id)
                      }}
                    >
                      <p.icon size={18} aria-hidden className="pages-icon" />
                      <span className="pages-label">{p.label}</span>
                      <span className="pages-hint">{p.hint}</span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </div>
  )
}
