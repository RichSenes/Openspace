import { useEffect, type RefObject } from 'react'

/**
 * Closes a popup with Escape or a press anywhere outside `box`. Escape gives the focus back
 * to `trigger`, so a keyboard user is left where they opened it.
 */
export function useDismiss(
  open: boolean,
  close: () => void,
  box: RefObject<HTMLElement | null>,
  trigger?: RefObject<HTMLElement | null>,
) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      close()
      trigger?.current?.focus()
    }
    const onPress = (e: PointerEvent) => {
      if (!box.current?.contains(e.target as Node)) close()
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('pointerdown', onPress)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPress)
    }
  }, [open, close, box, trigger])
}
