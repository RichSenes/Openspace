import {
  Bot,
  Brain,
  Briefcase,
  Building2,
  Contact,
  DoorOpen,
  Earth,
  FolderCog,
  Grid3x3,
  MessageCircle,
  MonitorCog,
  PencilRuler,
  Repeat,
  ScrollText,
  Settings,
  ShieldCheck,
  SquareKanban,
  type LucideIcon,
} from 'lucide-react'

/**
 * Openspace's pages, opened over the world from the bar: Shiori and her brain on their own,
 * the rest in menus by group. Which pages stay, move or merge is decided when they are
 * reviewed one by one.
 */

export type View = 'shiori' | 'brain' | 'board' | 'routines' | 'reports' | 'characters' | 'agents' | 'rooms' | 'office' | 'building' | 'lab-edit' | 'worlds' | 'security' | 'settings'

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
  icon: LucideIcon
  pages: PageInfo[]
}

/** One place in the bar: a page of its own, or a group's menu. */
export type NavItem = { page: PageInfo } | { group: PageGroup }

const page = (id: View, label: string, hint: string, icon: LucideIcon, perOffice = false): PageInfo => ({ id, label, hint, icon, perOffice })

export const NAV: NavItem[] = [
  { page: page('shiori', 'Shiori', 'Talk to her, her sessions and her look', MessageCircle) },
  { page: page('brain', 'Brain', 'The notes she keeps and how they link', Brain) },
  {
    group: {
      label: 'Work',
      icon: Briefcase,
      pages: [
        page('board', 'Board', 'Every task, from proposed to done', SquareKanban, true),
        page('routines', 'Routines', 'Recurring jobs and their runs', Repeat, true),
        page('reports', 'Reports', 'A day, an epic, a changelog', ScrollText, true),
      ],
    },
  },
  {
    group: {
      label: 'Offices',
      icon: Building2,
      pages: [
        page('characters', 'Characters', 'Who they are, mood, agenda', Contact, true),
        page('agents', 'Agents', 'The roles desks can take', Bot),
        page('rooms', 'Rooms', 'Rooms and their desks', DoorOpen, true),
        page('office', 'Office', 'Project, files, models, limits', FolderCog, true),
        page('building', 'Building', 'The old view of one office', Grid3x3, true),
      ],
    },
  },
  {
    group: {
      label: 'World',
      icon: Earth,
      pages: [page('lab-edit', 'Edit the lab', 'Add, remove, move and dress things', PencilRuler), page('worlds', 'Worlds', 'The kind of world the lab lives in', Earth)],
    },
  },
  {
    group: {
      label: 'System',
      icon: MonitorCog,
      pages: [page('security', 'Security', 'Audits of the code', ShieldCheck, true), page('settings', 'Settings', 'Engines, accounts, voice, everything else', Settings)],
    },
  },
]

/** Every page, in the bar's order, each in one group (Shiori and Brain together as Mind). */
export const GROUPS: PageGroup[] = [{ label: 'Mind', icon: MessageCircle, pages: NAV.flatMap((n) => ('page' in n ? [n.page] : [])) }, ...NAV.flatMap((n) => ('group' in n ? [n.group] : []))]

export const PAGES: PageInfo[] = GROUPS.flatMap((g) => g.pages)

export const pageInfo = (view: View): PageInfo => PAGES.find((p) => p.id === view)!

/** The page an address asks for (`#board`), or none: the world. */
export function pageFromHash(hash: string): View | null {
  const id = decodeURIComponent(hash.replace(/^#\/?/, '')).trim().toLowerCase()
  return PAGES.find((p) => p.id === id)?.id ?? null
}
