import { useCallback, useState, type ReactNode } from 'react'
import { ToastContext } from './toastContext'
import './toasts.css'

interface Toast {
  id: number
  text: string
}

/** Confirmations ("Task queued") show at the bottom and leave on their own. Errors stay inline. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const show = useCallback((text: string) => {
    const id = Date.now() + Math.random()
    setToasts((all) => [...all, { id, text }])
    setTimeout(() => setToasts((all) => all.filter((t) => t.id !== id)), 3000)
  }, [])

  return (
    <ToastContext.Provider value={show}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className="toast">
            {t.text}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
