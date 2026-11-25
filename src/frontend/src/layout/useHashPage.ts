import { useCallback, useEffect, useRef, useState } from 'react'
import { pageFromHash, type View } from './pageList'

/** How opening a page changes the address: nothing, a new step from the lab, or a swap of pages. */
export function openStep(now: View | null, view: View): 'none' | 'push' | 'replace' {
  return now === view ? 'none' : now === null ? 'push' : 'replace'
}

/** How closing changes it: back a step when the page was opened from the lab, else the address is cleared. */
export function closeStep(now: View | null, stepped: boolean): 'none' | 'back' | 'replace' {
  return now === null ? 'none' : stepped ? 'back' : 'replace'
}

/**
 * The page open over the lab, kept in the address (`#board`) so it can be bookmarked and the
 * browser's back closes it. A page opened from the lab is a step in the history; switching
 * between pages replaces it, so back always returns to the lab.
 */
export function useHashPage(): [View | null, (view: View) => void, () => void] {
  const [page, setPage] = useState(() => pageFromHash(location.hash))
  // Whether the page open was a step taken from the lab in this visit, so closing can step back.
  const stepped = useRef(false)

  useEffect(() => {
    const on = () => {
      const next = pageFromHash(location.hash)
      if (!next) stepped.current = false
      setPage(next)
    }
    addEventListener('hashchange', on)
    return () => removeEventListener('hashchange', on)
  }, [])

  const open = useCallback((view: View) => {
    const step = openStep(pageFromHash(location.hash), view)
    if (step === 'push') {
      stepped.current = true
      location.hash = view
    } else if (step === 'replace') {
      history.replaceState(history.state, '', `#${view}`)
      setPage(view)
    }
  }, [])

  const close = useCallback(() => {
    const step = closeStep(pageFromHash(location.hash), stepped.current)
    if (step === 'back') history.back()
    else if (step === 'replace') {
      history.replaceState(history.state, '', location.pathname + location.search)
      setPage(null)
    }
  }, [])

  return [page, open, close]
}
