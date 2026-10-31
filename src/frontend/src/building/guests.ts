import type { DeskView, Guest, OfficeGuests, OfficeView } from '../api/types'
import type { AgentState } from '../art/agentStates'

/** Guests' desks have ids of their own, never an agent's. */
export const GUEST_PREFIX = 'guest:'

export const isGuestDesk = (deskId: string) => deskId.startsWith(GUEST_PREFIX)

/** Replaces one office's guests with the new list. */
export function mergeGuests(list: Guest[] | undefined, update: OfficeGuests): Guest[] {
  return [...(list ?? []).filter((g) => g.officeId !== update.officeId), ...update.guests]
}

/** What a guest is shown doing at its laptop. */
export function guestStateOf(guest: Guest): AgentState {
  return guest.state === 'Working' ? 'working' : guest.state === 'Reading' ? 'reading' : 'idle'
}

/** A guest's name tag: the folder it works in, or plain "Guest" at the repository's root or when it stays anonymous. */
export function guestName(guest: Guest): string {
  return guest.folder?.split(/[\\/]/).filter(Boolean).pop() ?? 'Guest'
}

/** A guest at a laptop in the guest room: picked looks, from its id like an agent's. */
export function guestDesk(guest: Guest): DeskView {
  return { id: `${GUEST_PREFIX}${guest.id}`, name: guestName(guest), agentName: 'guest', model: null, roleId: null, spot: null }
}

/** The office as the building draws it: its guests seated in the guest room. */
export function withGuests(office: OfficeView, guests: readonly Guest[] | undefined): OfficeView {
  const own = (guests ?? []).filter((g) => g.officeId === office.id)
  if (!office.rooms.some((r) => r.kind === 'Guests')) return office
  return { ...office, rooms: office.rooms.map((r) => (r.kind === 'Guests' ? { ...r, desks: own.map(guestDesk) } : r)) }
}
