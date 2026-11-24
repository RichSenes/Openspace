import type { View } from '../../../layout/TopBar'

/** Opens one of Openspace's own pages, where everything about it is. */
export function openPage(view: View) {
  try {
    localStorage.setItem('openspace.view', view)
  } catch {
    // Not remembered: the app opens on its own page.
  }
  location.href = '/'
}
