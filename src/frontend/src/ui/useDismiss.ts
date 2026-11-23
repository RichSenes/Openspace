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
      const at = e.target as Node
      // A press on the button that opened it is left to that button, which toggles it shut;
      // closing here too would make its click open it again.
      if (box.current?.contains(at) || trigger?.current?.contains(at)) return
      close()
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('pointerdown', onPress)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPress)
    }
  }, [open, close, box, trigger])
}
