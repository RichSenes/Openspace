import { useSyncExternalStore } from 'react'

/** Whether a CSS media query matches now, following the window as it is resized. */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query)
      list.addEventListener('change', onChange)
      return () => list.removeEventListener('change', onChange)
    },
    () => window.matchMedia(query).matches,
    () => false,
  )
}

/** The width below which the layout changes instead of shrinking (see layout.css). */
export const NARROW = '(max-width: 1100px)'
