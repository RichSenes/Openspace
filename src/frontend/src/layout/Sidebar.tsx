import { useState, type FormEvent } from 'react'
import { Plus } from 'lucide-react'
import { useOpenOffice } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { useToast } from '../ui/toastContext'

interface Props {
  offices: OfficeView[]
  selectedId: string | undefined
  onSelect: (id: string) => void
}

export function Sidebar({ offices, selectedId, onSelect }: Props) {
  const [adding, setAdding] = useState(false)
  const [name, setName] = useState('')
  const openOffice = useOpenOffice()
  const toast = useToast()

  const submit = (e: FormEvent) => {
    e.preventDefault()
    openOffice.mutate(name, {
      onSuccess: ({ id }) => {
        toast(`${name.trim()} opened`)
        setName('')
        setAdding(false)
        onSelect(id)
      },
    })
  }

  return (
    <aside className="sidebar">
      <h2 className="sidebar-title pixel">Offices</h2>
      <ul className="office-list">
        {offices.map((o) => (
          <li key={o.id}>
            <button
              type="button"
              className="office-item"
              aria-current={o.id === selectedId ? 'true' : undefined}
              onClick={() => onSelect(o.id)}
            >
              <span>{o.name}</span>
              <span className="hint">
                {o.rooms.length} {o.rooms.length === 1 ? 'room' : 'rooms'}
              </span>
            </button>
          </li>
        ))}
      </ul>

      {adding ? (
        <form className="sidebar-form" onSubmit={submit}>
          <label className="field">
            <span>Office name</span>
            <input className="input" value={name} onChange={(e) => setName(e.target.value)} autoFocus required />
          </label>
          {openOffice.error && <p className="error">{openOffice.error.message}</p>}
          <div className="row">
            <button type="submit" className="btn btn-primary" disabled={openOffice.isPending}>
              Open office
            </button>
            <button type="button" className="btn btn-quiet" onClick={() => setAdding(false)}>
              Cancel
            </button>
          </div>
        </form>
      ) : (
        <button type="button" className="btn" onClick={() => setAdding(true)}>
          <Plus size={16} /> New office
        </button>
      )}
    </aside>
  )
}
