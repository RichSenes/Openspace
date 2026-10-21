import { useState, type FormEvent } from 'react'
import { Plus } from 'lucide-react'
import { ApiError } from '../api/client'
import {
  INITIALIZABLE,
  useAddDesk,
  useOpenRoom,
  useRenameDesk,
  useRoles,
  useSetDeskRole,
  useSetRepository,
  useUpdateRoomContext,
} from '../api/hooks'
import type { DeskView, OfficeView, RoleView, RoomView } from '../api/types'
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

      <RepositoryForm office={office} />

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

function RepositoryForm({ office }: { office: OfficeView }) {
  const [path, setPath] = useState(office.repositoryPath ?? '')
  const setRepository = useSetRepository(office.id)
  const toast = useToast()

  const save = (initialize: boolean) =>
    setRepository.mutate(
      { path, initialize },
      { onSuccess: () => toast(initialize ? 'Git initialized and repository saved' : 'Repository saved') },
    )

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save(false)
  }

  const error = setRepository.error
  const canInitialize = error instanceof ApiError && error.code !== undefined && INITIALIZABLE.has(error.code)

  return (
    <div className="repo">
    <form className="room-new" onSubmit={submit}>
      <label className="field">
        <span>Repository folder (absolute path of the git repo agents work on)</span>
        <input
          className="input mono"
          value={path}
          onChange={(e) => setPath(e.target.value)}
          placeholder="/home/you/repos/project"
          required
        />
      </label>
      <button type="submit" className="btn" disabled={path === (office.repositoryPath ?? '') || setRepository.isPending}>
        Save repository
      </button>
    </form>
      {error && (
        <div className="repo-problem">
          <p className="error">{error.message}</p>
          {canInitialize && (
            <button type="button" className="btn btn-primary" disabled={setRepository.isPending} onClick={() => save(true)}>
              Initialize git here
            </button>
          )}
        </div>
      )}
      {!office.repositoryPath && !error && (
        <p className="hint">Until this is set, tasks of this office cannot start.</p>
      )}
    </div>
  )
}

function RoomCard({ officeId, room }: { officeId: string; room: RoomView }) {
  const [context, setContext] = useState(room.context)
  const [desk, setDesk] = useState({ name: '', agentName: 'build', model: '', roleId: '' })
  const updateContext = useUpdateRoomContext(officeId)
  const addDesk = useAddDesk(officeId)
  const roles = useRoles()
  const toast = useToast()

  const saveContext = (e: FormEvent) => {
    e.preventDefault()
    updateContext.mutate({ roomId: room.id, context }, { onSuccess: () => toast('Context saved') })
  }

  const submitDesk = (e: FormEvent) => {
    e.preventDefault()
    addDesk.mutate(
      { roomId: room.id, name: desk.name, agentName: desk.agentName, model: desk.model.trim() || null, roleId: desk.roleId || null },
      {
        onSuccess: () => {
          toast(`${desk.name.trim()} added to ${room.name}`)
          setDesk({ name: '', agentName: 'build', model: '', roleId: '' })
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
              <DeskItem key={d.id} officeId={officeId} desk={d} roles={roles.data ?? []} />
            ))}
          </ul>
        )}

        <form className="desk-new" onSubmit={submitDesk}>
          <label className="field">
            <span>Desk name: who sits here, in words you like</span>
            <input
              className="input"
              value={desk.name}
              onChange={(e) => setDesk({ ...desk, name: e.target.value })}
              placeholder="e.g. Theme developer"
              required
            />
          </label>
          <label className="field">
            <span>Role</span>
            <select className="input" value={desk.roleId} onChange={(e) => setDesk({ ...desk, roleId: e.target.value })}>
              <option value="">No role</option>
              {roles.data?.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </select>
          </label>
          <details className="advanced">
            <summary className="hint">Advanced: OpenCode engine and model</summary>
            <label className="field">
              <span>OpenCode agent that runs the desk (build works, plan only reads)</span>
              <input
                className="input mono"
                value={desk.agentName}
                onChange={(e) => setDesk({ ...desk, agentName: e.target.value })}
                required
              />
            </label>
            <label className="field">
              <span>Model (optional, wins over the role&apos;s)</span>
              <input
                className="input mono"
                value={desk.model}
                onChange={(e) => setDesk({ ...desk, model: e.target.value })}
                placeholder="provider/model"
              />
            </label>
          </details>
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

function DeskItem({ officeId, desk, roles }: { officeId: string; desk: DeskView; roles: RoleView[] }) {
  const [renaming, setRenaming] = useState(false)
  const [name, setName] = useState(desk.name)
  const rename = useRenameDesk(officeId)
  const setRole = useSetDeskRole(officeId)
  const toast = useToast()
  const role = roles.find((r) => r.id === desk.roleId)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    rename.mutate(
      { deskId: desk.id, name },
      {
        onSuccess: () => {
          toast(`Renamed to ${name.trim()}`)
          setRenaming(false)
        },
      },
    )
  }

  return (
    <li>
      {renaming ? (
        <form className="desk-rename" onSubmit={submit}>
          <input className="input" value={name} onChange={(e) => setName(e.target.value)} aria-label="Desk name" required autoFocus />
          <button type="submit" className="btn btn-primary" disabled={rename.isPending}>
            Save
          </button>
          <button type="button" className="btn btn-quiet" onClick={() => { setRenaming(false); setName(desk.name) }}>
            Cancel
          </button>
        </form>
      ) : (
        <div className="desk-line">
          <span>{desk.name}</span>
          <button type="button" className="btn btn-quiet desk-rename-btn" onClick={() => setRenaming(true)}>
            Rename
          </button>
        </div>
      )}
      <span className="hint">
        {role ? role.name : 'No role'} · engine <span className="mono">{desk.agentName}</span>
        {desk.model && <span className="mono"> · {desk.model}</span>}
      </span>
      <label className="desk-role">
        <span className="sr-only">Role of {desk.name}</span>
        <select
          className="input"
          value={desk.roleId ?? ''}
          disabled={setRole.isPending}
          onChange={(e) =>
            setRole.mutate(
              { deskId: desk.id, roleId: e.target.value || null },
              { onSuccess: () => toast(`${desk.name}: role changed, from its next task`) },
            )
          }
        >
          <option value="">No role</option>
          {roles.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </select>
      </label>
      {(rename.error ?? setRole.error) && <p className="error">{(rename.error ?? setRole.error)!.message}</p>}
    </li>
  )
}
