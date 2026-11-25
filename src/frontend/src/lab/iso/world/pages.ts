import type { View } from '../../../layout/pageList'

/** Where to land on a page: an office, a task on its board. */
export interface PageAt {
  officeId?: string
  taskId?: string
}

let opener: ((view: View, at?: PageAt) => void) | null = null

/** The app's home sets how pages open over the lab; returns how to stop. */
export function setPageOpener(open: (view: View, at?: PageAt) => void) {
  opener = open
  return () => {
    if (opener === open) opener = null
  }
}

/**
 * Opens one of Openspace's own pages, where everything about it is: over the lab on the home,
 * or, from the lab on its own (`?lab=world`), in the app at that page's address.
 */
export function openPage(view: View, at?: PageAt) {
  if (opener) opener(view, at)
  else location.href = `/#${view}`
}
