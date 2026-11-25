import { useCallback, useEffect, useState } from 'react'
import { AgentsPanel } from './agents/AgentsPanel'
import { useOffices } from './api/hooks'
import type { OfficeView } from './api/types'
import { Board } from './board/Board'
import { BrainPage } from './brain/BrainPage'
import { BuildingView } from './building/BuildingView'
import { Sidebar } from './layout/Sidebar'
import { TopBar } from './layout/TopBar'
import { PageSheet } from './layout/PageSheet'
import { pageInfo, type View } from './layout/pageList'
import { useHashPage } from './layout/useHashPage'
import { useSoundCues } from './sounds/useSoundCues'
import { useSupervisorJobCues } from './supervisor/useSupervisorJobCues'
import { useBuildingHub } from './realtime/useBuildingHub'
import { ReportsPanel } from './reports/ReportsPanel'
import { SettingsPanel } from './settings/SettingsPanel'
import { RoomsPanel } from './rooms/RoomsPanel'
import { RoutinesPanel } from './routines/RoutinesPanel'
import { ShioriPage } from './shiori/ShioriPage'
import { SecurityPanel } from './security/SecurityPanel'
import { CharactersPanel } from './characters/CharactersPanel'
import { OfficePanel } from './office/OfficePanel'
import { FirstSteps } from './setup/FirstSteps'
import { onOpenSettings, requestSettingsSection } from './settings/sections'
import { usePersistentState } from './usePersistentState'
import { WorldHome } from './lab/iso/world/WorldLab'
import { setPageOpener, type PageAt } from './lab/iso/world/pages'
import './layout/layout.css'

/**
 * Openspace: the lab fills the window, the bar is one thin line over it, and every page opens
 * over the lab from the bar's menu (or from the lab itself: a character's card, Shiori).
 */
export default function App() {
  const link = useBuildingHub()
  useSoundCues()
  useSupervisorJobCues()
  const offices = useOffices()
  const [page, openView, close] = useHashPage()
  const [selectedId, setSelectedId] = usePersistentState<string>('openspace.office', '')
  const office = offices.data?.find((o) => o.id === selectedId) ?? offices.data?.[0]
  /** A task to open on the Board, picked elsewhere (an agent in the lab, what waits for you). */
  const [focusTask, setFocusTask] = useState<string>()

  const open = useCallback(
    (view: View, at?: PageAt) => {
      if (at?.officeId) setSelectedId(at.officeId)
      setFocusTask(at?.taskId)
      openView(view)
    },
    [openView, setSelectedId],
  )
  // A view (a routine's form, say) can send the person to a part of Settings; the lab opens pages too.
  useEffect(() => onOpenSettings(() => open('settings')), [open])
  useEffect(() => setPageOpener(open), [open])

  return (
    <div className="shell home" data-view={page ?? 'world'}>
      <TopBar page={page} onPage={(v) => open(v)} onHome={close} link={link} onOpen={(target) => open(target.view, { officeId: target.officeId, taskId: target.taskId })} />
      <div className="home-world" inert={page !== null}>
        <WorldHome paused={page !== null} />
      </div>
      <PageSheet
        page={page}
        onClose={close}
        render={(view) => (
          <div className={pageInfo(view).perOffice ? 'body' : 'body is-alone'}>
            {pageInfo(view).perOffice && <Sidebar offices={offices.data ?? []} selectedId={office?.id} onSelect={setSelectedId} />}
            <main className="main">
              <Page view={view} office={office} offices={offices} focusTask={focusTask} open={open} onSelect={setSelectedId} />
            </main>
          </div>
        )}
      />
    </div>
  )
}

interface PageProps {
  view: View
  office: OfficeView | undefined
  offices: ReturnType<typeof useOffices>
  focusTask: string | undefined
  open: (view: View, at?: PageAt) => void
  onSelect: (officeId: string) => void
}

/** One page, as it always was: those about an office wait for the offices and ask for one first. */
function Page({ view, office, offices, focusTask, open, onSelect }: PageProps) {
  const toSettings = (section: Parameters<typeof requestSettingsSection>[0]) => {
    requestSettingsSection(section)
    open('settings')
  }
  if (view === 'agents') return <AgentsPanel onOpenEngines={() => toSettings('engines')} />
  if (view === 'settings') return <SettingsPanel />
  if (view === 'shiori') return <ShioriPage onOpenSettings={toSettings} />
  if (view === 'brain') return <BrainPage />
  if (offices.isPending) {
    return (
      <div className="empty">
        <p>Opening the building…</p>
      </div>
    )
  }
  if (offices.error) {
    return (
      <div className="empty">
        <h2>The building is unreachable</h2>
        <p>Openspace could not reach its server: {offices.error.message}. Check that the API is running.</p>
      </div>
    )
  }
  if (!office) {
    return (
      <div className="first-steps-wrap">
        <FirstSteps office={undefined} onSelect={onSelect} onView={(v) => open(v)} />
      </div>
    )
  }
  switch (view) {
    case 'board':
      return <Board key={`${office.id}-${focusTask ?? ''}`} office={office} initialTaskId={focusTask} />
    case 'rooms':
      return <RoomsPanel key={office.id} office={office} onOpenOffice={() => open('office')} />
    case 'office':
      return <OfficePanel key={office.id} office={office} />
    case 'routines':
      return <RoutinesPanel key={office.id} office={office} />
    case 'reports':
      return <ReportsPanel key={office.id} office={office} />
    case 'characters':
      return <CharactersPanel key={office.id} office={office} />
    case 'security':
      return <SecurityPanel key={office.id} office={office} />
    default:
      return <BuildingView office={office} onView={(v) => open(v)} onOpenRoutines={() => open('routines')} onOpenTask={(taskId, officeId) => open('board', { officeId, taskId })} />
  }
}
