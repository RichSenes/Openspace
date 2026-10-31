import { X } from 'lucide-react'
import type { Guest, OfficeView } from '../api/types'
import { guestDesk, guestName } from './guests'
import { Portrait } from './LookDialog'

const DOING: Record<Guest['state'], string> = {
  Working: 'Working',
  Reading: 'Reading the code',
  Idle: 'Waiting for its person',
}

const time = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })

/** Who a guest is: a session someone started outside Openspace, watched but never driven. */
export function GuestCard({ guest, office, style, onClose }: { guest: Guest; office: OfficeView; style: React.CSSProperties; onClose: () => void }) {
  const room = office.rooms.find((r) => r.kind === 'Guests')?.name ?? 'Guest room'
  const where = guest.folder === null ? null : guest.folder === '' ? 'the whole repository' : guest.folder
  return (
    <section className="agent-card" style={style} role="dialog" aria-label={`${guestName(guest)}, ${room}`}>
      <header className="agent-card-head">
        <Portrait desk={guestDesk(guest)} theme={office.theme} visitor />
        <div>
          <h2>{guestName(guest)}</h2>
          <p className="hint">{room} · Claude Code, started outside Openspace</p>
        </div>
        <button type="button" className="btn btn-quiet" onClick={onClose} aria-label="Close">
          <X size={16} />
        </button>
      </header>
      <p>
        <strong>{DOING[guest.state]}</strong>, here since {time(guest.since)}.
      </p>
      {where ? (
        <p className="hint">
          Works in {where}
          {guest.branch ? `, on ${guest.branch}` : ''}.
        </p>
      ) : (
        <p className="hint">This office has private folders: where a guest works stays private.</p>
      )}
      <p className="hint">Openspace only sees it: it cannot pause it, answer it or give it tasks. It leaves after a quarter of an hour of silence.</p>
    </section>
  )
}
