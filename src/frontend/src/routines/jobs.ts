/**
 * A job offers routine's settings as the editor works on them. Read from the routine's JSON and
 * written back into it: keys the editor does not know stay as they were.
 */

export type JapaneseLevel = 'none' | 'basic' | 'conversational' | 'business' | 'fluent'

/** Where offers come from: a portal, a search on one, or a company's own job page. */
export type BoardKind = 'japan-dev' | 'tokyodev' | 'linkedin' | 'daijob' | 'company'

export interface Board {
  kind: BoardKind
  /** The words searched, or "lever:acme" for a company page; empty for a whole portal. */
  value: string
  /** Every job of a company page counts as in Japan, whatever its place says. */
  allInJapan: boolean
}

export interface Jobs {
  title: string
  channel: string
  language: string
  model: string
  profile: string
  boards: Board[]
  roles: string[]
  avoid: string[]
  visa: boolean
  japaneseUpTo: JapaneseLevel
  /** Yen a year; 0 for none. */
  minSalary: number
  cities: string[]
  most: number
  fit: number
  inbox: string
  offers: string
  rest: Record<string, unknown>
}

export const BOARD_KINDS: { value: BoardKind; label: string; hint: string; placeholder?: string }[] = [
  { value: 'linkedin', label: 'LinkedIn search', hint: 'Its public job search in Japan, no account. Add "@Osaka" for a city.', placeholder: 'full stack developer' },
  { value: 'japan-dev', label: 'Japan Dev', hint: 'Jobs in Japan for people who speak English, read whole.' },
  { value: 'tokyodev', label: 'TokyoDev', hint: 'Read from its sitemap: company and title only, its pages are closed to readers. Paste an offer in the chat to have it judged in full.' },
  { value: 'daijob', label: 'Daijob search', hint: 'Bilingual jobs, searched in English; most ask for business Japanese.', placeholder: 'software engineer' },
  { value: 'company', label: 'Company page', hint: 'A company\'s own job page: lever:acme, greenhouse:acme, ashby:acme, workable:acme or workday:tenant.wd1/Site.', placeholder: 'lever:acme' },
]

export const JAPANESE_LEVELS: { value: JapaneseLevel; label: string }[] = [
  { value: 'none', label: 'None' },
  { value: 'basic', label: 'Basic' },
  { value: 'conversational', label: 'Conversational' },
  { value: 'business', label: 'Business' },
  { value: 'fluent', label: 'Fluent' },
]

const KEYS = ['title', 'channel', 'language', 'model', 'profile', 'boards', 'roles', 'avoid', 'visa', 'japaneseUpTo', 'minSalary', 'cities', 'most', 'fit', 'inbox', 'offers']
const LEVELS: JapaneseLevel[] = ['none', 'basic', 'conversational', 'business', 'fluent']

type Json = Record<string, unknown>
const isObject = (v: unknown): v is Json => typeof v === 'object' && v !== null && !Array.isArray(v)
const text = (v: unknown, fallback = ''): string => (typeof v === 'string' ? v : fallback)
const texts = (v: unknown): string[] => (Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [])
const whole = (v: unknown, min: number, max: number, fallback: number): number =>
  typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max ? v : fallback

/** A board as settings name it, "linkedin:react", read into its kind and value. */
export function boardOf(name: string, allInJapan = false): Board {
  const trimmed = name.trim()
  const lower = trimmed.toLowerCase()
  if (lower === 'japan-dev' || lower === 'tokyodev') return { kind: lower, value: '', allInJapan }
  if (lower.startsWith('linkedin:')) return { kind: 'linkedin', value: trimmed.slice('linkedin:'.length), allInJapan }
  if (lower.startsWith('daijob:')) return { kind: 'daijob', value: trimmed.slice('daijob:'.length), allInJapan }
  return { kind: 'company', value: trimmed, allInJapan }
}

/** The board's name in the settings: "linkedin:react", "tokyodev", "lever:acme". */
export function boardName(board: Board): string {
  switch (board.kind) {
    case 'japan-dev':
    case 'tokyodev':
      return board.kind
    case 'linkedin':
    case 'daijob':
      return `${board.kind}:${board.value.trim()}`
    default:
      return board.value.trim()
  }
}

/** The settings, or undefined while they are not a JSON object. */
export function readJobs(settings: string): Jobs | undefined {
  let parsed: unknown
  try {
    parsed = JSON.parse(settings)
  } catch {
    return undefined
  }
  if (!isObject(parsed)) return undefined
  const boards = Array.isArray(parsed.boards)
    ? parsed.boards.flatMap((b): Board[] =>
        typeof b === 'string' ? [boardOf(b)] : isObject(b) && typeof b.board === 'string' ? [boardOf(b.board, b.allInJapan === true)] : [],
      )
    : []
  const level = text(parsed.japaneseUpTo).trim().toLowerCase()
  return {
    title: text(parsed.title, 'Job offers'),
    channel: text(parsed.channel),
    language: text(parsed.language, 'English'),
    model: text(parsed.model, 'local/gemma'),
    profile: text(parsed.profile, '~/Documents/CV/profile.md'),
    boards,
    roles: texts(parsed.roles),
    avoid: texts(parsed.avoid),
    visa: parsed.visa !== false,
    japaneseUpTo: LEVELS.includes(level as JapaneseLevel) ? (level as JapaneseLevel) : 'conversational',
    minSalary: whole(parsed.minSalary, 0, 100_000_000, 0),
    cities: texts(parsed.cities),
    most: whole(parsed.most, 1, 40, 8),
    fit: whole(parsed.fit, 1, 5, 3),
    inbox: text(parsed.inbox, '~/Documents/CV/inbox'),
    offers: text(parsed.offers, '~/Documents/CV/offers'),
    rest: Object.fromEntries(Object.entries(parsed).filter(([k]) => !KEYS.includes(k))),
  }
}

/** The settings JSON, with every key the editor did not know as it was. */
export function writeJobs(jobs: Jobs): string {
  const { rest, boards, ...known } = jobs
  return JSON.stringify(
    {
      ...rest,
      ...known,
      boards: boards.map((b) => (b.kind === 'company' && b.allInJapan ? { board: boardName(b), allInJapan: true } : boardName(b))),
    },
    null,
    2,
  )
}

/** What keeps the settings from being saved, in words for the person; empty when nothing does. */
export function jobsProblems(jobs: Jobs): string[] {
  const problems: string[] = []
  if (jobs.boards.length === 0) problems.push('Add at least one place to look for offers.')
  jobs.boards.forEach((b, i) => {
    if ((b.kind === 'linkedin' || b.kind === 'daijob') && !b.value.trim()) problems.push(`Search ${i + 1} needs words to look for.`)
    if (b.kind === 'company' && !/^(lever|greenhouse|ashby|workable|workday):\S+$/i.test(b.value.trim()))
      problems.push(`Company page ${i + 1} is written as service and company, such as lever:acme.`)
  })
  const names = jobs.boards.map(boardName).map((n) => n.toLowerCase())
  if (new Set(names).size !== names.length) problems.push('A place to look is there twice.')
  if (!jobs.profile.trim()) problems.push('Say where your profile is: the judge reads it.')
  return problems
}

/** "¥5M" for 5,000,000 yen a year; "none" for 0. */
export function yen(amount: number): string {
  if (amount <= 0) return 'none'
  const millions = amount / 1_000_000
  return `¥${Number.isInteger(millions) ? millions : millions.toFixed(1)}M`
}
