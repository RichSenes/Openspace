import { useEffect, useRef, type ReactNode } from 'react'
import { X } from 'lucide-react'
import './dialog.css'

interface Props {
  title: string
  open: boolean
  onClose: () => void
  children: ReactNode
}

/** Native modal dialog: focus trapping and Escape come from the browser. */
export function Dialog({ title, open, onClose, children }: Props) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog ref={ref} className="dialog" onClose={onClose} aria-label={title}>
      <header className="dialog-head">
        <h2>{title}</h2>
        <button type="button" className="btn btn-quiet" onClick={onClose} aria-label="Close">
          <X size={16} />
        </button>
      </header>
      {open && children}
    </dialog>
  )
}
