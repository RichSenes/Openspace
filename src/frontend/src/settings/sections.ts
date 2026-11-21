/** The parts of Settings, grouped by what the person wants to do, in the order of the index. */
export type SettingsSection = 'engines' | 'brain' | 'local' | 'models' | 'agents' | 'connections' | 'look' | 'system'

export const SECTIONS: { id: SettingsSection; label: string; hint: string }[] = [
  { id: 'engines', label: 'Engines', hint: 'What runs the agents, and your Claude account' },
  { id: 'brain', label: 'Brain', hint: 'Shiori\'s vault: where it is and what it holds' },
  { id: 'local', label: 'Local AI', hint: 'Models on this machine and who comes first on the card' },
  { id: 'models', label: 'Models', hint: 'Which engine and model does each job: Shiori, reviews, digests, journals' },
  { id: 'agents', label: 'How agents work', hint: 'Permissions and how many work at once' },
  { id: 'connections', label: 'Connections', hint: 'Telegram and Google accounts for the routines, set up step by step' },
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

const OPEN = 'open'

/** Asks the page to show Settings at a section, from a view that cannot change the view itself. */
export function openSettings(section: SettingsSection) {
  requestSettingsSection(section)
  bus.dispatchEvent(new Event(OPEN))
}

/** Calls back when a view asks to open Settings; gives back how to stop listening. */
export function onOpenSettings(callback: () => void): () => void {
  bus.addEventListener(OPEN, callback)
  return () => bus.removeEventListener(OPEN, callback)
}
