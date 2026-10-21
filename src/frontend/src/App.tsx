import { AgentsPanel } from './agents/AgentsPanel'
import { useOffices } from './api/hooks'
import { Board } from './board/Board'
import { BuildingView } from './building/BuildingView'
import { Sidebar } from './layout/Sidebar'
import { TopBar, type View } from './layout/TopBar'
import { useBuildingHub } from './realtime/useBuildingHub'
import { RoomsPanel } from './rooms/RoomsPanel'
import { usePersistentState } from './usePersistentState'
import './layout/layout.css'

export default function App() {
  const link = useBuildingHub()
  const offices = useOffices()
  const [view, setView] = usePersistentState<View>('openspace.view', 'building')
  const [selectedId, setSelectedId] = usePersistentState<string>('openspace.office', '')
  const office = offices.data?.find((o) => o.id === selectedId) ?? offices.data?.[0]

  return (
    <div className="shell">
      <TopBar view={view} onView={setView} link={link} />
      <div className="body">
        <Sidebar offices={offices.data ?? []} selectedId={office?.id} onSelect={setSelectedId} />
        <main className="main">
          {view === 'agents' ? (
            <AgentsPanel />
          ) : offices.isPending ? (
            <div className="empty">
              <p>Opening the building…</p>
            </div>
          ) : offices.error ? (
            <div className="empty">
              <h2>The building is unreachable</h2>
              <p>Openspace could not reach its server: {offices.error.message}. Check that the API is running.</p>
            </div>
          ) : !office ? (
            <div className="empty">
              <h2>No offices yet</h2>
              <p>An office is a project. Open one from the side panel to start furnishing it.</p>
            </div>
          ) : view === 'board' ? (
            <Board key={office.id} office={office} />
          ) : view === 'rooms' ? (
            <RoomsPanel key={office.id} office={office} />
          ) : (
            <BuildingView office={office} onOpenRooms={() => setView('rooms')} />
          )}
        </main>
      </div>
    </div>
  )
}
