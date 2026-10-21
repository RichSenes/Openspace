import { useState, type FormEvent } from 'react'
import { Plus } from 'lucide-react'
import { useAddDesk, useOpenRoom, useUpdateRoomContext } from '../api/hooks'
import type { OfficeView, RoomView } from '../api/types'
import { useToast } from '../ui/toastContext'
import './rooms.css'

export function RoomsPanel({ office }: { office: OfficeView }) {
  const [name, setName] = useState('')
  const openRoom = useOpenRoom(office.id)
  const toast = useToast()

  const submit = (e: FormEvent) => {
    e.preventDefault()
    openRoom.mutate(name, {
      onSuccess: () => {
        toast(`${name.trim()} opened`)
        setName('')
      },
    })
  }

  return (
    <section className="rooms">
      <header className="rooms-head">
        <h1>Rooms of {office.name}</h1>
        <p className="hint">Each room is an area of competence. Its desks are the agents that work there.</p>
      </header>

      <form className="room-new" onSubmit={submit}>
        <label className="field">
          <span>New room</span>
          <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Web, Identity, Compliance" required />
        </label>
        <button type="submit" className="btn btn-primary" disabled={openRoom.isPending}>
          <Plus size={16} /> Open room
        </button>
        {openRoom.error && <p className="error">{openRoom.error.message}</p>}
      </form>

      {office.rooms.length === 0 ? (
        <p className="hint">No rooms yet. Open the first one above.</p>
      ) : (
        <div className="room-grid">
          {office.rooms.map((room) => (
            <RoomCard key={room.id} officeId={office.id} room={room} />
          ))}
        </div>
      )}
    </section>
  )
}

function RoomCard({ officeId, room }: { officeId: string; room: RoomView }) {
  const [context, setContext] = useState(room.context)
  const [desk, setDesk] = useState({ name: '', agentName: '', model: '' })
  const updateContext = useUpdateRoomContext(officeId)
  const addDesk = useAddDesk(officeId)
  const toast = useToast()

  const saveContext = (e: FormEvent) => {
    e.preventDefault()
    updateContext.mutate({ roomId: room.id, context }, { onSuccess: () => toast('Context saved') })
  }

  const submitDesk = (e: FormEvent) => {
    e.preventDefault()
    addDesk.mutate(
      { roomId: room.id, name: desk.name, agentName: desk.agentName, model: desk.model.trim() || null },
      {
        onSuccess: () => {
          toast(`${desk.name.trim()} added to ${room.name}`)
          setDesk({ name: '', agentName: '', model: '' })
        },
      },
    )
  }

  return (
    <article className="room-card">
      <h2>{room.name}</h2>

      <form className="stack" onSubmit={saveContext}>
        <label className="field">
          <span>Context for every agent in this room</span>
          <textarea
            className="input"
            value={context}
            onChange={(e) => setContext(e.target.value)}
            placeholder="Conventions, paths, what not to touch…"
          />
        </label>
        <div className="row">
          <button type="submit" className="btn" disabled={context === room.context || updateContext.isPending}>
            Save context
          </button>
        </div>
        {updateContext.error && <p className="error">{updateContext.error.message}</p>}
      </form>

      <section className="desks" aria-label={`Desks in ${room.name}`}>
        <h3 className="pixel desks-title">Desks</h3>
        {room.desks.length === 0 ? (
          <p className="hint">No desks yet: nobody works here.</p>
        ) : (
          <ul className="desk-list">
            {room.desks.map((d) => (
              <li key={d.id}>
                <span>{d.name}</span>
                <span className="mono hint">
                  {d.agentName}
                  {d.model && ` · ${d.model}`}
                </span>
              </li>
            ))}
          </ul>
        )}

        <form className="desk-new" onSubmit={submitDesk}>
          <label className="field">
            <span>Desk name</span>
            <input className="input" value={desk.name} onChange={(e) => setDesk({ ...desk, name: e.target.value })} required />
          </label>
          <label className="field">
            <span>OpenCode agent</span>
            <input
              className="input mono"
              value={desk.agentName}
              onChange={(e) => setDesk({ ...desk, agentName: e.target.value })}
              placeholder="frontend-dev"
              required
            />
          </label>
          <label className="field">
            <span>Model (optional)</span>
            <input
              className="input mono"
              value={desk.model}
              onChange={(e) => setDesk({ ...desk, model: e.target.value })}
              placeholder="provider/model"
            />
          </label>
          <div className="row">
            <button type="submit" className="btn" disabled={addDesk.isPending}>
              <Plus size={16} /> Add desk
            </button>
          </div>
          {addDesk.error && <p className="error">{addDesk.error.message}</p>}
        </form>
      </section>
    </article>
  )
}
