import { useEffect, useRef, useState, type ReactNode } from 'react'
import { X } from 'lucide-react'
import { pageInfo, type View } from './pageList'
import './home.css'

/** How long the sheet takes to slide away; kept in the page meanwhile. */
const OUT_MS = 220

/**
 * A page of Openspace over the lab: it slides in from the right, the lab dimmed and still
 * alive behind it. The ×, Escape, a click on the lab or the browser's back close it.
 */
export function PageSheet({ page, onClose, render }: { page: View | null; onClose: () => void; render: (view: View) => ReactNode }) {
  // The page last shown stays while the sheet slides out.
  const [shown, setShown] = useState(page)
  if (page && page !== shown) setShown(page)
  useEffect(() => {
    if (page) return
    const id = setTimeout(() => setShown(null), OUT_MS)
    return () => clearTimeout(id)
  }, [page])

  const sheet = useRef<HTMLElement>(null)
  const back = useRef<HTMLElement | null>(null)
  // Opening takes the focus into the sheet; closing gives it back to where it was.
  useEffect(() => {
    if (!page) return
    back.current ??= document.activeElement as HTMLElement | null
    sheet.current?.focus({ preventScroll: true })
  }, [page])
  useEffect(() => {
    if (page) return
    back.current?.focus?.({ preventScroll: true })
    back.current = null
  }, [page])

  useEffect(() => {
    if (!page) return
    // Escape closes the page unless something inside it took the key first (a menu, a dialog).
    const key = (e: KeyboardEvent) => {
      if (e.key !== 'Escape' || e.defaultPrevented) return
      if (document.querySelector('dialog[open]')) return
      onClose()
    }
    addEventListener('keydown', key)
    return () => removeEventListener('keydown', key)
  }, [page, onClose])

  if (!shown) return null
  const info = pageInfo(shown)
  return (
    <>
      <div className={`sheet-shade${page ? ' is-open' : ''}`} onClick={onClose} aria-hidden />
      <section ref={sheet} className={`sheet${page ? ' is-open' : ''}`} aria-labelledby="sheet-title" tabIndex={-1}>
        <header className="sheet-head">
          <info.icon size={18} aria-hidden className="sheet-icon" />
          <h1 id="sheet-title" className="sheet-title">
            {info.label}
          </h1>
          <button type="button" className="sheet-close" onClick={onClose} aria-label={`Close ${info.label}, back to the lab`} title="Back to the lab (Esc)">
            <X size={18} aria-hidden />
          </button>
        </header>
        <div className="sheet-body" key={shown}>
          {render(shown)}
        </div>
      </section>
    </>
  )
}
