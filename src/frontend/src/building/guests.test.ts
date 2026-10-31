import { describe, expect, it } from 'vitest'
import type { Guest, OfficeView } from '../api/types'
import { guestName, guestStateOf, isGuestDesk, mergeGuests, withGuests } from './guests'

const guest = (id: string, officeId = 'e-corp', folder: string | null = 'src/web'): Guest => ({
  id,
  officeId,
  folder,
  branch: 'main',
  state: 'Working',
  since: '2015-06-24T09:00:00Z',
  lastAt: '2015-06-24T09:00:00Z',
})

const office: OfficeView = {
  id: 'e-corp',
  name: 'E Corp',
  theme: 'cozy',
  rooms: [
    { id: 'work', name: 'Work', context: '', procedure: [], desks: [] },
    { id: 'guests', name: 'Guest room', context: '', procedure: [], desks: [], kind: 'Guests' },
  ],
} as unknown as OfficeView

describe('guests', () => {
  it('sit in the guest room of their own office only', () => {
    const shown = withGuests(office, [guest('a'), guest('b', 'allsafe')])
    const desks = shown.rooms.find((r) => r.kind === 'Guests')!.desks
    expect(desks.map((d) => d.name)).toEqual(['web'])
    expect(isGuestDesk(desks[0].id)).toBe(true)
    expect(shown.rooms[0].desks).toEqual([])
  })

  it('stay anonymous without a folder, and show what they do', () => {
    expect(guestName(guest('a', 'e-corp', null))).toBe('Guest')
    expect(guestName(guest('a', 'e-corp', ''))).toBe('Guest')
    expect(guestStateOf({ ...guest('a'), state: 'Reading' })).toBe('reading')
    expect(guestStateOf({ ...guest('a'), state: 'Idle' })).toBe('idle')
  })

  it('are replaced office by office', () => {
    const list = mergeGuests([guest('a'), guest('b', 'allsafe')], { officeId: 'e-corp', guests: [guest('c')] })
    expect(list.map((g) => g.id).sort()).toEqual(['b', 'c'])
  })
})
