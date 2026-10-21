import { useState, type FormEvent } from 'react'
import { useCreateTask } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import type { DeskWithRoom } from './deskLookup'

interface Props {
  office: OfficeView
  desks: DeskWithRoom[]
  open: boolean
  onClose: () => void
}

export function NewTaskDialog({ office, desks, open, onClose }: Props) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [deskId, setDeskId] = useState('')
  const create = useCreateTask()
  const toast = useToast()

  const close = () => {
    create.reset()
    onClose()
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    create.mutate(
      { officeId: office.id, deskId: deskId || null, title, description },
      {
        onSuccess: () => {
          toast(deskId ? 'Task queued' : 'Task proposed')
          setTitle('')
          setDescription('')
          close()
        },
      },
    )
  }

  return (
    <Dialog title={`New task in ${office.name}`} open={open} onClose={close}>
      <form onSubmit={submit}>
        <label className="field">
          <span>What needs doing</span>
          <input className="input" value={title} onChange={(e) => setTitle(e.target.value)} required autoFocus />
        </label>
        <label className="field">
          <span>Details for the agent (optional)</span>
          <textarea className="input" value={description} onChange={(e) => setDescription(e.target.value)} />
        </label>
        <label className="field">
          <span>Desk</span>
          <select className="input" value={deskId} onChange={(e) => setDeskId(e.target.value)}>
            <option value="">No desk: propose it and decide later</option>
            {desks.map((d) => (
              <option key={d.id} value={d.id}>
                {d.roomName} · {d.name} ({d.agentName})
              </option>
            ))}
          </select>
        </label>
        {desks.length === 0 && <p className="hint">This office has no desks yet. Add them in Rooms.</p>}
        {create.error && <p className="error">{create.error.message}</p>}
        <div className="dialog-actions">
          <button type="button" className="btn btn-quiet" onClick={close}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary" disabled={create.isPending}>
            {deskId ? 'Queue task' : 'Propose task'}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
