/** The parts of Settings, grouped by what the person wants to do, in the order of the index. */
export type SettingsSection = 'engines' | 'local' | 'agents' | 'connections' | 'look' | 'system'

export const SECTIONS: { id: SettingsSection; label: string; hint: string }[] = [
  { id: 'engines', label: 'Engines', hint: 'What runs the agents, and your Claude account' },
  { id: 'local', label: 'Local AI', hint: 'Models on this machine and who comes first on the card' },
  { id: 'agents', label: 'How agents work', hint: 'Permissions, how many at once, who plans and reviews' },
  { id: 'connections', label: 'Connections', hint: 'Google accounts for the routines' },
  { id: 'look', label: 'Look and sound', hint: 'Sounds and the weather at the windows' },
  { id: 'system', label: 'System', hint: 'Version and backups' },
]

export const SECTION_KEY = 'openspace.settings.section'
const EVENT = 'section'
/** Announcements between parts of the page; a module of its own, so tests need no browser. */
const bus = new EventTarget()

export function isSection(value: unknown): value is SettingsSection {
  return SECTIONS.some((s) => s.id === value)
}

/**
 * Asks Settings to show a section: remembered for when it opens, and announced to it when it
 * is already open. Storage may be refused (private mode): the announcement still goes.
 */
export function requestSettingsSection(section: SettingsSection) {
  try {
    localStorage.setItem(SECTION_KEY, section)
  } catch {
    // Not remembered; an open Settings still hears the event.
  }
  bus.dispatchEvent(new CustomEvent<SettingsSection>(EVENT, { detail: section }))
}

/** Calls back when another part of the page asks for a section; gives back how to stop listening. */
export function onSectionRequest(callback: (section: SettingsSection) => void): () => void {
  const listener = (e: Event) => {
    const section = (e as CustomEvent<unknown>).detail
    if (isSection(section)) callback(section)
  }
  bus.addEventListener(EVENT, listener)
  return () => bus.removeEventListener(EVENT, listener)
}
