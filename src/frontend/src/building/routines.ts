import type { DeskActivity, DeskView, OfficeView, RoutineView } from '../api/types'
import type { AgentState } from '../art/agentStates'

/** A routine's place in the routines room: a karakuri doll at a desk with an hourglass. */
export function routineDesk(routine: RoutineView): DeskView {
  return {
    id: routine.id,
    name: routine.name,
    agentName: routine.kind,
    model: null,
    roleId: null,
    spot: null,
    look: { skin: 'fair', hair: 'black', hairStyle: 'short', cloth: 'red', costume: 'karakuri' },
  }
}

/** The office as the building draws it: its routines seated in the routines room. */
export function withRoutines(office: OfficeView, routines: readonly RoutineView[] | undefined): OfficeView {
  if (!routines?.length || !office.rooms.some((r) => r.kind === 'Routines')) return office
  return { ...office, rooms: office.rooms.map((r) => (r.kind === 'Routines' ? { ...r, desks: routines.map(routineDesk) } : r)) }
}

/**
 * How a routine's doll looks: working while it runs, waving while proposals wait for a
 * yes, troubled after a failed run, asleep until its next time, idle when turned off.
 */
export function routineStateOf(routine: RoutineView, running: boolean, waiting: number): AgentState {
  if (running || routine.lastRun?.status === 'Running') return 'working'
  if (waiting > 0) return 'waiting'
  if (routine.lastRun?.status === 'Failed') return 'failed'
  return routine.enabled ? 'queued' : 'idle'
}

/** What a routine's bubble says: its current step while it runs, or the proposals waiting. */
export function routineBubble(routine: RoutineView, line: string | undefined, waiting: number): DeskActivity | undefined {
  const base = { officeId: routine.officeId, deskId: routine.id, taskId: '', step: null, permissionId: null, at: new Date().toISOString() }
  if (line) return { ...base, kind: 'Working', doing: line, headline: line }
  if (waiting > 0) {
    const headline = waiting === 1 ? '1 proposal waits for you' : `${waiting} proposals wait for you`
    return { ...base, kind: 'Waiting', doing: headline, headline, question: true }
  }
  return undefined
}
