import { useEffect, useState } from 'react'
import type { WorldView } from '../../../api/types'
import { ShioriPage } from '../../../shiori/ShioriPage'
import { useWake } from '../../../shiori/useWake'
import type { Heard } from '../../../shiori/voice'
import { loadByName } from '../../../shiori/wake'
import { Queue } from './Cards'
import { openPage } from './pages'

/**
 * Shiori inside the lab: clicking her (or calling her by name) slides in a small version of her
 * own page from the side, her figure on top and her conversation under it, with a tab for the
 * queue of who waits for you. The lab stays in view; closing it slides her away.
 */
export function ShioriPanel({
  open,
  onClose,
  world,
  pending,
  onPendingTaken,
}: {
  open: boolean
  onClose: () => void
  world: WorldView | undefined
  pending: Heard | null
  onPendingTaken: () => void
}) {
  const [tab, setTab] = useState<'talk' | 'waiting'>('talk')
  // Kept in the page a moment after closing, for the slide out.
  const [shown, setShown] = useState(open)
  if (open && !shown) setShown(true)
  useEffect(() => {
    if (open) return
    const id = setTimeout(() => setShown(false), 260)
    return () => clearTimeout(id)
  }, [open])
  useEffect(() => {
    if (!open) return
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !(e.target instanceof HTMLTextAreaElement)) onClose()
    }
    addEventListener('keydown', key)
    return () => removeEventListener('keydown', key)
  }, [open, onClose])
  if (!shown && !open) return null
  const waiting = world?.waiting.length ?? 0
  return (
    <aside className={`world-shiori${open ? ' is-open' : ''}`} aria-label="Shiori">
      <div className="world-shiori-tabs" role="tablist" aria-label="Shiori">
        <button type="button" role="tab" aria-selected={tab === 'talk'} onClick={() => setTab('talk')}>
          Talk
        </button>
        <button type="button" role="tab" aria-selected={tab === 'waiting'} onClick={() => setTab('waiting')}>
          Waiting{waiting ? ` (${waiting})` : ''}
        </button>
        <button type="button" className="world-shiori-close" onClick={onClose} aria-label="Close">
          ×
        </button>
      </div>
      {tab === 'talk' ? (
        <div className="world-shiori-talk">
          <ShioriPage onOpenSettings={() => openPage('settings')} pending={pending} onPendingTaken={onPendingTaken} />
        </div>
      ) : (
        <div className="world-shiori-waiting">
          <Queue world={world} />
        </div>
      )}
    </aside>
  )
}

/**
 * While her panel is closed, the lab still listens for her name (when the person turned that
 * on): "Shiori, …" opens her panel, and what was said after her name is the first thing she hears.
 */
export function WakeListener({ onCalled }: { onCalled: (heard: Heard) => void }) {
  const [on] = useState(loadByName)
  const { wake } = useWake(on, false, onCalled, () => {})
  if (wake !== 'talking' && wake !== 'hearing') return null
  return (
    <p className="world-wake" role="status">
      Shiori is listening…
    </p>
  )
}
