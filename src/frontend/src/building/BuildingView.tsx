import { useMemo, useState } from 'react'
import { Lightbulb, LightbulbOff, Move } from 'lucide-react'
import { activityByDesk } from '../activity/activity'
import { useActivity, useOfficeTasks, useSetOfficeTheme } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { THEMES, type ThemeName } from '../art/themes'
import { useToast } from '../ui/toastContext'
import { editFloor, editRoom, moveRoom, NO_DRAFTS, placeIn, resize, resizeFloor, setCut, withDrafts, type Drafts } from './arrange'
import { ArrangePanel } from './ArrangePanel'
import { BuildingCanvas, type Selection } from './BuildingCanvas'
import './building.css'

const lightsKey = (officeId: string) => `openspace.lights-off.${officeId}`

/** Whether this viewer keeps an office's lights off; remembered in this browser only. */
function useLightsOff(officeId: string): [boolean, (off: boolean) => void] {
  const read = () => {
    try {
      return localStorage.getItem(lightsKey(officeId)) === 'yes'
    } catch {
      return false
    }
  }
  const [state, setState] = useState({ officeId, off: read() })
  // Another office was opened: start from what this browser remembers for it.
  if (state.officeId !== officeId) setState({ officeId, off: read() })
  const set = (off: boolean) => {
    setState({ officeId, off })
    try {
      localStorage.setItem(lightsKey(officeId), off ? 'yes' : 'no')
    } catch {
      // Without storage the choice simply lasts until the page is closed.
    }
  }
  return [state.officeId === officeId ? state.off : read(), set]
}

interface Props {
  office: OfficeView
  onOpenRooms: () => void
}

export function BuildingView({ office, onOpenRooms }: Props) {
  const tasks = useOfficeTasks(office.id)
  const activity = useActivity()
  const bubbles = useMemo(() => activityByDesk(activity.data, office.id), [activity.data, office.id])
  const [lightsOff, setLightsOff] = useLightsOff(office.id)
  const setTheme = useSetOfficeTheme(office.id)
  const toast = useToast()
  const noTasks = useMemo(() => [], [])
  const [arranging, setArranging] = useState(false)
  const [drafts, setDrafts] = useState<Drafts>(NO_DRAFTS)
  const [selection, setSelection] = useState<Selection | null>(null)
  const shown = useMemo(() => withDrafts(office, drafts), [office, drafts])
  /** Changes the drafts from the office as they show it, whatever changed in between. */
  const edit = (change: (shown: OfficeView, drafts: Drafts) => Drafts) => setDrafts((d) => change(withDrafts(office, d), d))
  const stopArranging = () => {
    setArranging(false)
    setDrafts(NO_DRAFTS)
    setSelection(null)
  }

  if (office.rooms.length === 0) {
    return (
      <div className="empty">
        <h2>{office.name} is an empty floor</h2>
        <p>Open a room for each area of competence, then add the desks of the agents who work there.</p>
        <button type="button" className="btn btn-primary" onClick={onOpenRooms}>
          Set up rooms
        </button>
      </div>
    )
  }

  return (
    <div className={arranging ? 'building arranging' : 'building'}>
      <BuildingCanvas
        office={shown}
        tasks={tasks.data ?? noTasks}
        activities={bubbles}
        dark={lightsOff}
        arrange={
          arranging
            ? {
                selection,
                onSelect: setSelection,
                onPlace: (roomId, target, x, y) => edit((o, d) => placeIn(o, d, roomId, target, x, y)),
                onResize: (roomId, width, depth) => edit((o, d) => editRoom(o, d, roomId, (a) => resize(a, width, depth))),
                onMoveRoom: (roomId, left, top) => edit((o, d) => editFloor(o, d, (f, sizes) => moveRoom(f, sizes, roomId, left, top))),
                onResizeFloor: (width, depth) => edit((o, d) => editFloor(o, d, (f, sizes) => resizeFloor(f, sizes, width, depth))),
                onCut: (index, cut) => edit((o, d) => editFloor(o, d, (f, sizes) => setCut(f, sizes, index, cut))),
              }
            : undefined
        }
        controls={
          arranging ? undefined : (
          <>
            <button
              type="button"
              className="btn"
              onClick={() => {
                setArranging(true)
                setSelection({ roomId: office.rooms[0].id })
              }}
            >
              <Move size={16} /> Arrange
            </button>
            <button type="button" className="btn" aria-pressed={lightsOff} onClick={() => setLightsOff(!lightsOff)}>
              {lightsOff ? <LightbulbOff size={16} /> : <Lightbulb size={16} />} {lightsOff ? 'Lights on' : 'Lights off'}
            </button>
            <label className="sr-only" htmlFor="office-theme">
              Mood of {office.name}
            </label>
            <select
              id="office-theme"
              className="input"
              value={office.theme in THEMES ? office.theme : 'cozy'}
              disabled={setTheme.isPending}
              onChange={(e) => {
                const theme = e.target.value as ThemeName
                setTheme.mutate(theme, { onSuccess: () => toast(`${office.name} is now ${THEMES[theme].label}`) })
              }}
            >
              {(Object.keys(THEMES) as ThemeName[]).map((t) => (
                <option key={t} value={t}>
                  {THEMES[t].label}
                </option>
              ))}
            </select>
          </>
          )
        }
      />
      {arranging && (
        <ArrangePanel
          office={shown}
          saved={office}
          drafts={drafts}
          onEdit={edit}
          selection={selection}
          onSelect={setSelection}
          onClose={stopArranging}
        />
      )}
    </div>
  )
}
