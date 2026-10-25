import { useState, type FormEvent } from 'react'
import { Plus } from 'lucide-react'
import { ApiError } from '../api/client'
import {
  INITIALIZABLE,
  useAddDesk,
  useChangeDeskAgent,
  useEngines,
  useOpenRoom,
  useRenameDesk,
  useRoles,
  useSetDeskRole,
  useSetRepository,
  useSetRoomProcedure,
  useUpdateRoomContext,
  useSetRoomCheck,
  useSetPrivatePaths,
  useKeepLocal,
} from '../api/hooks'
import type { DeskView, OfficeView, RoleView, RoomView } from '../api/types'
import { LookDialog, Portrait } from '../building/LookDialog'
import { EngineFields } from '../engines/EngineFields'
import { ENGINE_LABEL, type EngineChoice } from '../engines/engines'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import { formatSteps, parseSteps } from './procedure'
import { MoodPicker } from '../building/MoodPicker'
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
      <LocalOnlyToggle office={office} />
      <PrivateFoldersForm key={(office.privatePaths ?? []).join('\n')} office={office} />

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
            <RoomCard key={room.id} officeId={office.id} theme={office.theme} room={room} />
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

function RoomCard({ officeId, theme, room }: { officeId: string; theme: string; room: RoomView }) {
  const [context, setContext] = useState(room.context)
  const fresh = { name: '', agentName: 'build', model: '', roleId: '', engine: 'OpenCode' as const }
  const [desk, setDesk] = useState<{ name: string; roleId: string } & EngineChoice>(fresh)
  const engines = useEngines()
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
      { roomId: room.id, name: desk.name, agentName: desk.agentName, model: desk.model.trim() || null, roleId: desk.roleId || null, engine: desk.engine },
      {
        onSuccess: () => {
          toast(`${desk.name.trim()} added to ${room.name}`)
          setDesk(fresh)
        },
      },
    )
  }

  return (
    <article className="room-card">
      <h2>{room.name}</h2>

      <MoodPicker officeId={officeId} officeTheme={theme} room={room} />

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

      <ProcedureForm officeId={officeId} room={room} />

      <CheckForm officeId={officeId} room={room} />

      <section className="desks" aria-label={`Desks in ${room.name}`}>
        <h3 className="pixel desks-title">Desks</h3>
        {room.desks.length === 0 ? (
          <p className="hint">No desks yet: nobody works here.</p>
        ) : (
          <ul className="desk-list">
            {room.desks.map((d) => (
              <DeskItem key={d.id} officeId={officeId} theme={theme} desk={d} roles={roles.data ?? []} />
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
          <EngineFields value={desk} onChange={(choice) => setDesk({ ...desk, ...choice })} engines={engines.data} />
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

function DeskItem({ officeId, theme, desk, roles }: { officeId: string; theme: string; desk: DeskView; roles: RoleView[] }) {
  const [renaming, setRenaming] = useState(false)
  const [dressing, setDressing] = useState(false)
  const [rewiring, setRewiring] = useState(false)
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
          <Portrait desk={desk} theme={theme} />
          <span>{desk.name}</span>
          <button type="button" className="btn btn-quiet desk-rename-btn" onClick={() => setRenaming(true)}>
            Rename
          </button>
          <button type="button" className="btn btn-quiet" onClick={() => setDressing(true)} aria-label={`Look of ${desk.name}`}>
            Look
          </button>
        </div>
      )}
      <span className="hint">
        {role ? role.name : 'No role'} · {ENGINE_LABEL[desk.engine ?? 'OpenCode']}
        {(desk.engine ?? 'OpenCode') === 'OpenCode' && desk.agentName !== 'build' && <span className="mono"> · {desk.agentName}</span>}
        {desk.model && <span className="mono"> · {desk.model}</span>}{' '}
        <button type="button" className="btn btn-quiet desk-engine-btn" onClick={() => setRewiring(true)} aria-label={`Engine of ${desk.name}`}>
          Change
        </button>
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
      <LookDialog officeId={officeId} theme={theme} desk={dressing ? desk : undefined} onClose={() => setDressing(false)} />
      <Dialog title={`What runs ${desk.name}`} open={rewiring} onClose={() => setRewiring(false)}>
        <EngineForm officeId={officeId} desk={desk} onDone={() => setRewiring(false)} />
      </Dialog>
    </li>
  )
}

/** Engine, model and agent of a desk, changed from its next task on. */
function EngineForm({ officeId, desk, onDone }: { officeId: string; desk: DeskView; onDone: () => void }) {
  const [choice, setChoice] = useState<EngineChoice>({ engine: desk.engine ?? 'OpenCode', agentName: desk.agentName, model: desk.model ?? '' })
  const engines = useEngines()
  const change = useChangeDeskAgent(officeId)
  const toast = useToast()

  const submit = (e: FormEvent) => {
    e.preventDefault()
    change.mutate(
      { deskId: desk.id, engine: choice.engine, agentName: choice.agentName, model: choice.model || null },
      {
        onSuccess: () => {
          toast(`${desk.name} now runs on ${ENGINE_LABEL[choice.engine]}, from its next task`)
          onDone()
        },
      },
    )
  }

  return (
    <form onSubmit={submit}>
      <EngineFields value={choice} onChange={setChoice} engines={engines.data} />
      {change.error && <p className="error">{change.error.message}</p>}
      <div className="row">
        <button type="button" className="btn" onClick={onDone}>
          Cancel
        </button>
        <button type="submit" className="btn btn-primary" disabled={change.isPending}>
          Save
        </button>
      </div>
    </form>
  )
}

/** Steps every task of the room must follow; agents report each one and work waits for them. */
function ProcedureForm({ officeId, room }: { officeId: string; room: RoomView }) {
  const [text, setText] = useState(formatSteps(room.procedure))
  const save = useSetRoomProcedure(officeId)
  const toast = useToast()
  const changed = formatSteps(parseSteps(text)) !== formatSteps(room.procedure)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate({ roomId: room.id, steps: parseSteps(text) }, { onSuccess: () => toast('Procedure saved, for tasks started from now on') })
  }

  return (
    <form className="stack" onSubmit={submit}>
      <label className="field">
        <span>Required procedure: one step per line. Work does not reach review until the agent reports every step.</span>
        <textarea
          className="input"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={'Before changing a file, copy it to /backup/<date>/\nAdd every change to CHANGES.md'}
        />
      </label>
      <div className="row">
        <button type="submit" className="btn" disabled={!changed || save.isPending}>
          Save procedure
        </button>
      </div>
      {save.error && <p className="error">{save.error.message}</p>}
    </form>
  )
}

/** The command that tells whether the room's code still works, run after merges. */
function CheckForm({ officeId, room }: { officeId: string; room: RoomView }) {
  const [command, setCommand] = useState(room.check ?? '')
  const save = useSetRoomCheck(officeId)
  const toast = useToast()
  const changed = command.trim() !== (room.check ?? '')

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate(
      { roomId: room.id, command: command.trim() || null },
      { onSuccess: () => toast(command.trim() ? 'Check saved: it runs after merges' : 'Check removed') },
    )
  }

  return (
    <form className="stack" onSubmit={submit}>
      <label className="field">
        <span>
          Check after merging: a command that fails when the room&apos;s code is broken. It runs from the repository folder in a
          fresh copy, so install what it needs first.
        </span>
        <input
          className="input mono"
          value={command}
          onChange={(e) => setCommand(e.target.value)}
          placeholder="cd src/web && npm ci && npm test"
        />
      </label>
      <div className="row">
        <button type="submit" className="btn" disabled={!changed || save.isPending}>
          Save check
        </button>
      </div>
      {save.error && <p className="error">{save.error.message}</p>}
    </form>
  )
}

/**
 * Folders of yours no agent of this office may touch: Openspace and both engines refuse
 * them, and every agent is told so. One absolute path per line.
 */
function PrivateFoldersForm({ office }: { office: OfficeView }) {
  const saved = (office.privatePaths ?? []).join('\n')
  const [text, setText] = useState(saved)
  const save = useSetPrivatePaths(office.id)
  const toast = useToast()
  const paths = text.split('\n').map((l) => l.trim()).filter(Boolean)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate(paths, { onSuccess: () => toast(paths.length ? `${paths.length} private ${paths.length === 1 ? 'folder' : 'folders'} saved` : 'No private folders now') })
  }

  return (
    <form className="stack private-folders" onSubmit={submit}>
      <label className="field">
        <span>
          Private folders: one absolute path per line. No agent of this office may open, list, read, search or change them;
          Openspace refuses any request that names them without asking you, and each agent is told so.
        </span>
        <textarea className="input mono" value={text} onChange={(e) => setText(e.target.value)} placeholder={'/home/you/photos\n/home/you/ai/models'} />
      </label>
      <div className="row">
        <button type="submit" className="btn" disabled={paths.join('\n') === saved || save.isPending}>
          Save private folders
        </button>
      </div>
      {save.error && <p className="error">{save.error.message}</p>}
    </form>
  )
}

/** Keeps the project on this machine: Openspace then never pushes it nor opens pull requests. */
function LocalOnlyToggle({ office }: { office: OfficeView }) {
  const keep = useKeepLocal(office.id)
  const toast = useToast()
  return (
    <div className="stack">
      <label className="check-row">
        <input
          type="checkbox"
          checked={Boolean(office.localOnly)}
          disabled={keep.isPending}
          onChange={(e) =>
            keep.mutate(e.target.checked, {
              onSuccess: () => toast(e.target.checked ? `${office.name} stays on this machine` : `${office.name} can be pushed again`),
            })
          }
        />
        <span>Keep this project on this machine: Openspace never pushes it or opens pull requests.</span>
      </label>
      {keep.error && <p className="error">{keep.error.message}</p>}
    </div>
  )
}
