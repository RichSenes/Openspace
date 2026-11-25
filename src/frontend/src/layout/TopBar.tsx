import { useCallback, useLayoutEffect, useRef, useState, type KeyboardEvent as KeyEvent } from 'react'
import { ChevronDown, LayoutGrid, type LucideIcon } from 'lucide-react'
import { SupervisorStatus } from '../supervisor/SupervisorStatus'
import { SetupCheck } from '../setup/SetupCheck'
import { NeedsYou } from '../activity/NeedsYou'
import { ReleaseBanner } from '../release/ReleaseBanner'
import { MuteButton } from '../sounds/MuteButton'
import { ClaudeMeter, GpuStatus, LimitPauses } from '../engines/ClaudeUsage'
import type { LinkState } from '../realtime/useBuildingHub'
import type { NeedsYouTarget } from '../activity/needs'
import { useDismiss } from '../ui/useDismiss'
import { GROUPS, NAV, pageInfo, type PageGroup, type View } from './pageList'
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
        <Nav page={page} onPage={onPage} />
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

/**
 * The pages in the bar: Shiori and Brain as buttons of their own, the other groups as menus.
 * On narrow windows they all go under one Pages menu instead.
 */
function Nav({ page, onPage }: { page: View | null; onPage: (view: View) => void }) {
  const current = page ? pageInfo(page) : null
  return (
    <div className="nav-wrap">
      <nav className="nav" aria-label="Pages">
        {NAV.map((n) =>
          'page' in n ? (
            <button key={n.page.id} type="button" className="nav-button" aria-current={n.page.id === page ? 'page' : undefined} onClick={() => onPage(n.page.id)}>
              <n.page.icon size={16} aria-hidden />
              <span>{n.page.label}</span>
            </button>
          ) : (
            <Menu key={n.group.label} id={`menu-${n.group.label.toLowerCase()}`} label={n.group.label} icon={n.group.icon} groups={[n.group]} page={page} onPage={onPage} />
          ),
        )}
      </nav>
      <div className="nav-narrow">
        <Menu id="menu-pages" label={current?.label ?? 'Pages'} icon={current?.icon ?? LayoutGrid} groups={GROUPS} page={page} onPage={onPage} wide />
      </div>
    </div>
  )
}

interface MenuProps {
  id: string
  label: string
  icon: LucideIcon
  groups: PageGroup[]
  page: View | null
  onPage: (view: View) => void
  /** Every group, in two columns, headed by their names. */
  wide?: boolean
}

/** A button and its list of pages; arrows move between them, Escape or a click elsewhere closes. */
function Menu({ id, label, icon: Icon, groups, page, onPage, wide = false }: MenuProps) {
  const [open, setOpen] = useState(false)
  // Opened from the keyboard, the focus goes into the menu; from the pointer it stays put.
  const [byKey, setByKey] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const panel = useRef<HTMLDivElement>(null)
  const close = useCallback(() => setOpen(false), [])
  useDismiss(open, close, box, button)
  const holds = groups.some((g) => g.pages.some((p) => p.id === page))

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
        className={`nav-button${holds ? ' is-current' : ''}`}
        aria-expanded={open}
        aria-controls={id}
        onClick={(e) => {
          setByKey(e.detail === 0)
          setOpen(!open)
        }}
      >
        <Icon size={16} aria-hidden />
        <span>{label}</span>
        <ChevronDown size={14} aria-hidden className="pages-chevron" />
      </button>
      {open && (
        <div id={id} className={`pages-menu${wide ? ' is-wide' : ''}`} ref={panel} onKeyDown={onKey}>
          {groups.map((g) => (
            <section key={g.label} className="pages-group" aria-label={g.label}>
              {wide && <h2>{g.label}</h2>}
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
