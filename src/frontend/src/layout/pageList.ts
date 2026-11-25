import { Bot, Brain, Building2, Contact, DoorOpen, Map, MessageCircle, Repeat, ScrollText, Settings, ShieldCheck, SquareKanban, type LucideIcon } from 'lucide-react'

/**
 * Openspace's pages, opened over the world from the menu in the bar. Grouped for now; which
 * ones stay, move or merge is decided when they are reviewed one by one.
 */

export type View = 'building' | 'shiori' | 'brain' | 'board' | 'rooms' | 'routines' | 'reports' | 'characters' | 'security' | 'office' | 'agents' | 'settings'

export interface PageInfo {
  id: View
  label: string
  /** One line in the menu: what the page is for. */
  hint: string
  icon: LucideIcon
  /** About one office: the page shows the office list beside it. */
  perOffice: boolean
}

export interface PageGroup {
  label: string
  pages: PageInfo[]
}

export const GROUPS: PageGroup[] = [
  {
    label: 'Mind',
    pages: [
      { id: 'shiori', label: 'Shiori', hint: 'Talk to her, her sessions and her look', icon: MessageCircle, perOffice: false },
      { id: 'brain', label: 'Brain', hint: 'The notes she keeps and how they link', icon: Brain, perOffice: false },
    ],
  },
  {
    label: 'Work',
    pages: [
      { id: 'board', label: 'Board', hint: 'Every task, from proposed to done', icon: SquareKanban, perOffice: true },
      { id: 'routines', label: 'Routines', hint: 'Recurring jobs and their runs', icon: Repeat, perOffice: true },
      { id: 'reports', label: 'Reports', hint: 'A day, an epic, a changelog', icon: ScrollText, perOffice: true },
    ],
  },
  {
    label: 'People and places',
    pages: [
      { id: 'characters', label: 'Characters', hint: 'Who they are, mood, agenda', icon: Contact, perOffice: true },
      { id: 'agents', label: 'Agents', hint: 'The roles desks can take', icon: Bot, perOffice: false },
      { id: 'rooms', label: 'Rooms', hint: 'Rooms and their desks', icon: DoorOpen, perOffice: true },
      { id: 'office', label: 'Office', hint: 'Project, files, models, limits', icon: Building2, perOffice: true },
      { id: 'building', label: 'Building', hint: 'The old view of one office', icon: Map, perOffice: true },
    ],
  },
  {
    label: 'System',
    pages: [
      { id: 'security', label: 'Security', hint: 'Audits of the code', icon: ShieldCheck, perOffice: true },
      { id: 'settings', label: 'Settings', hint: 'Engines, accounts, voice, everything else', icon: Settings, perOffice: false },
    ],
  },
]

export const PAGES: PageInfo[] = GROUPS.flatMap((g) => g.pages)

export const pageInfo = (view: View): PageInfo => PAGES.find((p) => p.id === view)!

/** The page an address asks for (`#board`), or none: the world. */
export function pageFromHash(hash: string): View | null {
  const id = decodeURIComponent(hash.replace(/^#\/?/, '')).trim().toLowerCase()
  return PAGES.find((p) => p.id === id)?.id ?? null
}
