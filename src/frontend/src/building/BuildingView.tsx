import { useMemo, useRef, useState } from 'react'
import { Lightbulb, LightbulbOff, Map as MapIcon, Move } from 'lucide-react'
import { activityByDesk } from '../activity/activity'
import { contextByDesk } from '../activity/energy'
import { internsByDesk } from '../activity/interns'
import { useActivity, useDeskContexts, useGuests, useInterns, useWeather, useOfficeTasks, useProposals, useRoutines, useSetOfficeTheme } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { THEMES, type ThemeName } from '../art/themes'
import { useToast } from '../ui/toastContext'
import { editFloor, editRoom, moveRoom, NO_DRAFTS, placeIn, resize, resizeFloor, setCut, withDrafts, type Drafts } from './arrange'
import { desksOf } from '../board/deskLookup'
import { NewTaskDialog } from '../board/NewTaskDialog'
import { ActivityFeed } from './ActivityFeed'
import { AgentCard } from './AgentCard'
import { ArrangePanel } from './ArrangePanel'
import { BuildingCanvas, type DeskPick, type Selection } from './BuildingCanvas'
import { LookDialog } from './LookDialog'
import { useRoutineProgress } from '../realtime/routineProgress'
import { routineBubble, routineStateOf, withRoutines } from './routines'
import { guestDesk, guestStateOf, isGuestDesk, withGuests } from './guests'
import { GuestCard } from './GuestCard'
import { usePersistentState } from '../usePersistentState'
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
  /** Shows the office's routines: a click on one of their dolls. */
  onOpenRoutines: () => void
  /** Shows a task on the Board. */
  /** Opens a task on the board; the office is given when it may be another one. */
  onOpenTask: (taskId: string, officeId?: string) => void
}

/** Where the card of a clicked agent goes: beside the click, kept inside the building. */
function cardPlace(pick: DeskPick, box: DOMRect | undefined): React.CSSProperties {
  const width = box?.width ?? 1000
  const height = box?.height ?? 600
  const left = pick.x + 16 + 320 < width ? pick.x + 16 : Math.max(16, pick.x - 16 - 320)
  return { left, top: Math.max(16, Math.min(pick.y - 40, height - 340)) }
}

export function BuildingView({ office, onOpenRooms, onOpenRoutines, onOpenTask }: Props) {
  const tasks = useOfficeTasks(office.id)
  const activity = useActivity()
  const energy = useDeskContexts()
  const contexts = useMemo(() => contextByDesk(energy.data, office.id), [energy.data, office.id])
  const helpers = useInterns()
  const weather = useWeather().data?.now?.sky
  const interns = useMemo(() => internsByDesk(helpers.data, office.id), [helpers.data, office.id])
  const routines = useRoutines(office.id)
  const guests = useGuests()
  const ownGuests = useMemo(() => (guests.data ?? []).filter((g) => g.officeId === office.id), [guests.data, office.id])
  const proposals = useProposals(office.id)
  const progress = useRoutineProgress()
  const desksBubbles = useMemo(() => activityByDesk(activity.data, office.id), [activity.data, office.id])
  // The routines' dolls: their state, and their bubbles beside the desks'.
  const routineStates = useMemo(() => {
    const waiting = (id: string) => proposals.data?.filter((p) => p.routineId === id).length ?? 0
    // Guests too: they never take tasks, their state comes from their own session.
    return new Map([
      ...(routines.data ?? []).map((r) => [r.id, routineStateOf(r, progress.has(r.id), waiting(r.id))] as const),
      ...ownGuests.map((g) => [guestDesk(g).id, guestStateOf(g)] as const),
    ])
  }, [routines.data, proposals.data, progress, ownGuests])
  const bubbles = useMemo(() => {
    const all = new Map(desksBubbles)
    for (const r of routines.data ?? []) {
      const said = routineBubble(r, progress.get(r.id)?.line, proposals.data?.filter((p) => p.routineId === r.id).length ?? 0)
      if (said) all.set(r.id, said)
    }
    return all
  }, [desksBubbles, routines.data, proposals.data, progress])
  const [lightsOff, setLightsOff] = useLightsOff(office.id)
  const setTheme = useSetOfficeTheme(office.id)
  const toast = useToast()
  const noTasks = useMemo(() => [], [])
  const [arranging, setArranging] = useState(false)
  const [map, setMap] = usePersistentState<'yes' | 'no'>('openspace.minimap', 'yes')
  const [drafts, setDrafts] = useState<Drafts>(NO_DRAFTS)
  const [selection, setSelection] = useState<Selection | null>(null)
  // While arranging, the routines room and the guest room show empty: who sits there places itself.
  const shown = useMemo(
    () => (arranging ? withDrafts(office, drafts) : withGuests(withRoutines(withDrafts(office, drafts), routines.data), ownGuests)),
    [office, drafts, arranging, routines.data, ownGuests],
  )
  const wrapper = useRef<HTMLDivElement>(null)
  const [picked, setPicked] = useState<{ pick: DeskPick; style: React.CSSProperties }>()
  const [giving, setGiving] = useState<string>()
  const [dressing, setDressing] = useState<string>()
  const desks = useMemo(() => desksOf(office), [office])
  const pickedDesk = desks.find((d) => d.id === picked?.pick.deskId)
  const pickedGuest = ownGuests.find((g) => guestDesk(g).id === picked?.pick.deskId)
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
    <div className={arranging ? 'building arranging' : 'building'} ref={wrapper}>
      <BuildingCanvas
        onPickDesk={(pick) => {
          if (pick && isGuestDesk(pick.deskId)) setPicked({ pick, style: cardPlace(pick, wrapper.current?.getBoundingClientRect()) })
          else if (pick && routineStates.has(pick.deskId)) onOpenRoutines()
          else setPicked(pick ? { pick, style: cardPlace(pick, wrapper.current?.getBoundingClientRect()) } : undefined)
        }}
        office={shown}
        tasks={tasks.data ?? noTasks}
        activities={bubbles}
        states={routineStates}
        contexts={contexts}
        interns={interns}
        dark={lightsOff}
        weather={weather}
        minimap={map === 'yes' && !arranging}
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
                setPicked(undefined)
                setArranging(true)
                setSelection({ roomId: office.rooms[0].id })
              }}
            >
              <Move size={16} /> Arrange
            </button>
            <button type="button" className="btn" aria-pressed={lightsOff} onClick={() => setLightsOff(!lightsOff)}>
              {lightsOff ? <LightbulbOff size={16} /> : <Lightbulb size={16} />} {lightsOff ? 'Lights on' : 'Lights off'}
            </button>
            <button type="button" className="btn" aria-pressed={map === 'yes'} onClick={() => setMap(map === 'yes' ? 'no' : 'yes')}>
              <MapIcon size={16} /> {map === 'yes' ? 'Hide map' : 'Map'}
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
      {pickedGuest && picked && !arranging && <GuestCard guest={pickedGuest} office={office} style={picked.style} onClose={() => setPicked(undefined)} />}
      {pickedDesk && picked && !arranging && (
        <AgentCard
          desk={pickedDesk}
          theme={office.theme}
          activity={bubbles.get(pickedDesk.id)}
          context={contexts.get(pickedDesk.id)}
          interns={interns.get(pickedDesk.id)}
          tasks={(tasks.data ?? []).filter((t) => t.deskId === pickedDesk.id)}
          style={picked.style}
          onClose={() => setPicked(undefined)}
          onOpenTask={onOpenTask}
          onNewTask={() => setGiving(pickedDesk.id)}
          onLook={() => setDressing(pickedDesk.id)}
        />
      )}
      {!arranging && <ActivityFeed key={office.id} office={office} onOpenTask={onOpenTask} />}
      {giving && <NewTaskDialog key={giving} office={office} desks={desks} open initialDeskId={giving} onClose={() => setGiving(undefined)} />}
      <LookDialog officeId={office.id} theme={office.theme} desk={desks.find((d) => d.id === dressing)} onClose={() => setDressing(undefined)} />
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
