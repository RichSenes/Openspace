import { useCallback, useRef, useState } from 'react'
import { ChevronDown, Plus } from 'lucide-react'
import type { OfficeView } from '../api/types'
import { NewOfficeForm } from '../office/NewOfficeForm'
import { useDismiss } from '../ui/useDismiss'

interface Props {
  offices: OfficeView[]
  selectedId: string | undefined
  onSelect: (id: string) => void
}

/**
 * The list of offices. On wide screens it is a column beside the view; below 1100 px it folds
 * into one line with the current office and a button that opens the list over the view, so
 * the view keeps the whole width and starts at the top of the screen.
 */
export function Sidebar({ offices, selectedId, onSelect }: Props) {
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const close = useCallback(() => setOpen(false), [])
  useDismiss(open, close, box, button)
  const current = offices.find((o) => o.id === selectedId)
  const [adding, setAdding] = useState(false)
  return (
    <aside className={open ? 'sidebar open' : 'sidebar'} ref={box}>
      <button
        ref={button}
        type="button"
        className="sidebar-switch"
        aria-expanded={open}
        aria-controls="office-list"
        onClick={() => setOpen(!open)}
      >
        <span className="sidebar-current">{current?.name ?? 'No office yet'}</span>
        <span className="sidebar-switch-action">
          {offices.length > 1 ? 'Switch office' : 'Offices'} <ChevronDown size={14} aria-hidden />
        </span>
      </button>
      <div className="sidebar-panel" id="office-list">
        <h2 className="sidebar-title pixel">Offices</h2>
        <ul className="office-list">
          {offices.map((o) => (
            <li key={o.id}>
              <button
                type="button"
                className="office-item"
                aria-current={o.id === selectedId ? 'true' : undefined}
                onClick={() => {
                  setOpen(false)
                  onSelect(o.id)
                }}
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
          <NewOfficeForm
            onOpened={(id) => {
              setAdding(false)
              setOpen(false)
              onSelect(id)
            }}
            onCancel={() => setAdding(false)}
          />
        ) : (
          <button type="button" className="btn" onClick={() => setAdding(true)}>
            <Plus size={16} /> New office
          </button>
        )}
      </div>
    </aside>
  )
}
