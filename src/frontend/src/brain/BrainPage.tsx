import { usePersistentState } from '../usePersistentState'
import { MapTab } from './MapTab'
import { WikiTab } from './WikiTab'
import './brain.css'

type Tab = 'wiki' | 'map'

/** Shiori's brain: the notes to read and write (Wiki), and how they link (Map). */
export function BrainPage() {
  const [tab, setTab] = usePersistentState<Tab>('openspace.brain.tab', 'wiki')
  const [path, setPath] = usePersistentState<string>('openspace.brain.note', '')

  return (
    <div className="brain">
      <header className="brain-head">
        <h1>Brain</h1>
        <div className="brain-tabs" role="tablist" aria-label="Brain">
          {(['wiki', 'map'] as const).map((t) => (
            <button
              key={t}
              type="button"
              role="tab"
              id={`brain-tab-${t}`}
              aria-selected={tab === t}
              aria-controls={`brain-panel-${t}`}
              className="brain-tab"
              onClick={() => setTab(t)}
            >
              {t === 'wiki' ? 'Wiki' : 'Map'}
            </button>
          ))}
        </div>
      </header>
      <div className="brain-panel" role="tabpanel" id={`brain-panel-${tab}`} aria-labelledby={`brain-tab-${tab}`}>
        {tab === 'wiki' ? (
          <WikiTab path={path || undefined} onOpen={(p) => setPath(p ?? '')} />
        ) : (
          <MapTab
            current={path || undefined}
            onOpen={(p) => {
              setPath(p)
              setTab('wiki')
            }}
          />
        )}
      </div>
    </div>
  )
}
