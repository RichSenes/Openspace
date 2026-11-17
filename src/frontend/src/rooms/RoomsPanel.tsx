import { useState, type FormEvent } from 'react'
import { Plus } from 'lucide-react'
import {
  useAddDesk,
  useChangeDeskAgent,
  useEngines,
  useOpenRoom,
  useRenameDesk,
  useRoles,
  useSetDeskRole,
  useSetRoomProcedure,
  useUpdateRoomContext,
  useSetRoomCheck,
} from '../api/hooks'
import type { DeskView, OfficeView, RoleView, RoomView } from '../api/types'
import { LookDialog, Portrait } from '../building/LookDialog'
import { EngineFields } from '../engines/EngineFields'
import { ENGINE_LABEL, type EngineChoice } from '../engines/engines'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import { parseSteps } from './procedure'
import { roomChanges, roomFormOf, type RoomForm } from './roomForm'
import { MoodPicker } from '../building/MoodPicker'
import './rooms.css'

/** The office's rooms and their desks; the office's own settings are in Office. */
export function RoomsPanel({ office, onOpenOffice }: { office: OfficeView; onOpenOffice: () => void }) {
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
        <p className="hint">
          Each room is an area of competence. Its desks are the agents that work there. The repository, private folders and reserve
          models are in{' '}
          <button type="button" className="link-button" onClick={onOpenOffice}>
            Office
          </button>
          .
        </p>
      </header>

      {!office.repositoryPath && (
        <div className="rooms-notice">
          <p>Tasks of this office cannot start until its repository is set.</p>
          <button type="button" className="btn btn-primary" onClick={onOpenOffice}>
            Set it in Office
          </button>
        </div>
      )}

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

function RoomCard({ officeId, theme, room }: { officeId: string; theme: string; room: RoomView }) {
  const fresh = { name: '', agentName: 'build', model: '', roleId: '', engine: 'OpenCode' as const }
  const [desk, setDesk] = useState<{ name: string; roleId: string } & EngineChoice>(fresh)
  const engines = useEngines()
  const addDesk = useAddDesk(officeId)
  const roles = useRoles()
  const toast = useToast()

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

      {room.kind !== 'Routines' && <MoodPicker officeId={officeId} officeTheme={theme} room={room} />}

      {room.kind !== 'Routines' && room.kind !== 'Guests' && <RoomSettings officeId={officeId} room={room} />}

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

        {room.kind === 'Routines' ? (
          <p className="hint">This room holds the office's routines, each as a karakuri at its desk. Add and change them in Routines.</p>
        ) : room.kind === 'Guests' ? (
          <p className="hint">
            Guests sit here: Claude Code sessions started outside Openspace that work in this office's repository, each at a laptop with a visitor's
            badge. They come and go by themselves; Openspace only sees them.
          </p>
        ) : (
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
        )}
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
/**
 * What every agent of the room is told, the steps its work must report and the check run
 * after merges: one form, one save, for the parts that changed.
 */
function RoomSettings({ officeId, room }: { officeId: string; room: RoomView }) {
  const [form, setForm] = useState<RoomForm>(() => roomFormOf(room))
  const context = useUpdateRoomContext(officeId)
  const procedure = useSetRoomProcedure(officeId)
  const check = useSetRoomCheck(officeId)
  const [problem, setProblem] = useState<string>()
  const [saving, setSaving] = useState(false)
  const toast = useToast()
  const changed = roomChanges(room, form)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setProblem(undefined)
    try {
      if (changed.includes('context')) await context.mutateAsync({ roomId: room.id, context: form.context })
      if (changed.includes('procedure')) await procedure.mutateAsync({ roomId: room.id, steps: parseSteps(form.procedure) })
      if (changed.includes('check')) await check.mutateAsync({ roomId: room.id, command: form.check.trim() || null })
      toast(changed.includes('procedure') ? `${room.name} saved: the procedure counts for tasks started from now on` : `${room.name} saved`)
    } catch (error) {
      setProblem((error as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="stack" onSubmit={submit}>
      <label className="field">
        <span>Context for every agent in this room</span>
        <textarea className="input" value={form.context} onChange={(e) => setForm({ ...form, context: e.target.value })} placeholder="Conventions, paths, what not to touch…" />
      </label>
      <label className="field">
        <span>Required procedure</span>
        <textarea
          className="input"
          value={form.procedure}
          onChange={(e) => setForm({ ...form, procedure: e.target.value })}
          placeholder={'Before changing a file, copy it to /backup/<date>/\nAdd every change to CHANGES.md'}
        />
        <span className="hint">One step per line. Work does not reach review until the agent reports every step.</span>
      </label>
      <label className="field">
        <span>Check after merging</span>
        <input className="input mono" value={form.check} onChange={(e) => setForm({ ...form, check: e.target.value })} placeholder="cd src/web && npm ci && npm test" />
        <span className="hint">
          A command that fails when the room&apos;s code is broken. It runs from the repository folder in a fresh copy, so install what it needs
          first.
        </span>
      </label>
      <div className="row">
        <button type="submit" className="btn" disabled={changed.length === 0 || saving}>
          {saving ? 'Saving…' : 'Save room'}
        </button>
        {changed.length > 0 && !saving && (
          <button type="button" className="btn btn-quiet" onClick={() => setForm(roomFormOf(room))}>
            Undo changes
          </button>
        )}
      </div>
      {problem && <p className="error">{problem}</p>}
    </form>
  )
}
