import { useEffect, useState } from 'react'
import type { WorldView } from '../../../api/types'
import { useWake } from '../../../shiori/useWake'
import type { Heard } from '../../../shiori/voice'
import { loadByName } from '../../../shiori/wake'
import { ShioriChat } from './ShioriChat'

/**
 * Shiori inside the lab: clicking her (or calling her by name) slides in her compact chat from
 * the side; the lab stays in view; closing it slides her away.
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
      if (e.key === 'Escape' && !(e.target instanceof HTMLInputElement)) onClose()
    }
    addEventListener('keydown', key)
    return () => removeEventListener('keydown', key)
  }, [open, onClose])
  if (!shown && !open) return null
  return (
    <aside className={`world-shiori${open ? ' is-open' : ''}`} aria-label="Shiori">
      <ShioriChat world={world} pending={pending} onPendingTaken={onPendingTaken} onClose={onClose} />
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
