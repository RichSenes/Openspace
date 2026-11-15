/**
 * A morning paper's settings as the editor works on them. Read from the routine's JSON and
 * written back into it: keys the editor does not know stay as they were.
 */

export type PictureSize = 'none' | 'small' | 'medium' | 'large'
export type Width = 'full' | 'half' | 'third'
export type Length = 'short' | 'normal' | 'long'
export type PictureAt = 'beside' | 'above'
export type Form = 'articles' | 'briefs' | 'feature' | 'explainer'

export interface Feed {
  name: string
  url: string
}

export interface PaperSection {
  name: string
  about: string
  feeds: Feed[]
  only: string
  focus: string[]
  keywords: string[]
  style: string
  width: Width
  /** Null: the paper's. */
  columns: number | null
  /** Null: the paper's. */
  picture: PictureSize | null
  pictureAt: PictureAt
  /** Null: the paper's. */
  articles: number | null
  length: Length
  form: Form
  /** Only news naming one of the keywords; without any, the section is left out that day. */
  onlyWithKeywords: boolean
  /** Null: the paper's. */
  hoursBack: number | null
  /** Keys of the section the editor does not know. */
  rest: Record<string, unknown>
}

export interface Paper {
  title: string
  subtitle: string
  language: string
  model: string
  folder: string
  channel: string
  reader: string
  rules: string[]
  priorities: string[]
  avoid: string[]
  hoursBack: number
  perSection: number
  goodNews: string[]
  layout: { columns: number; frontPicture: PictureSize; frontPictureAt: PictureAt; pictures: PictureSize; adapt: boolean }
  /** Phrases the writer never uses. */
  neverWrite: string[]
  /** One last read of every article for mistakes of grammar and spelling. */
  proofread: boolean
  sections: PaperSection[]
  /** Keys of the paper the editor does not know, the kanji among them. */
  rest: Record<string, unknown>
}

export const PICTURE_SIZES: { value: PictureSize; label: string }[] = [
  { value: 'none', label: 'None' },
  { value: 'small', label: 'Small' },
  { value: 'medium', label: 'Medium' },
  { value: 'large', label: 'Large' },
]

export const WIDTHS: { value: Width; label: string }[] = [
  { value: 'full', label: 'Full width' },
  { value: 'half', label: 'Half' },
  { value: 'third', label: 'A third' },
]

export const FORMS: { value: Form; label: string; hint: string }[] = [
  { value: 'articles', label: 'Articles', hint: 'Each story as an article, as long as the length below.' },
  { value: 'briefs', label: 'In brief', hint: 'Each story in one sentence, under a short headline: good for many small facts. No picture.' },
  { value: 'feature', label: 'Main story and briefs', hint: 'The most important story at length, with the picture; the others in one sentence.' },
  { value: 'explainer', label: 'Explained simply', hint: 'The most important story told by the questions a reader would ask; the others in one sentence.' },
]

export const LENGTHS: { value: Length; label: string; hint: string }[] = [
  { value: 'short', label: 'Short', hint: 'one or two sentences' },
  { value: 'normal', label: 'Normal', hint: 'two to four sentences' },
  { value: 'long', label: 'Long', hint: 'five to seven sentences' },
]

const SECTION_KEYS = ['name', 'about', 'feeds', 'only', 'focus', 'keywords', 'style', 'width', 'columns', 'picture', 'pictureAt', 'articles', 'length', 'onlyWithKeywords', 'hoursBack', 'form']
const PAPER_KEYS = [
  'title',
  'subtitle',
  'language',
  'model',
  'folder',
  'channel',
  'reader',
  'rules',
  'priorities',
  'avoid',
  'hoursBack',
  'perSection',
  'goodNews',
  'layout',
  'sections',
  'neverWrite',
  'proofread',
]

type Json = Record<string, unknown>

const isObject = (v: unknown): v is Json => typeof v === 'object' && v !== null && !Array.isArray(v)
const text = (v: unknown, fallback = ''): string => (typeof v === 'string' ? v : fallback)
const texts = (v: unknown): string[] => (Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [])
const whole = (v: unknown, min: number, max: number): number | null => (typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max ? v : null)
const oneOf = <T extends string>(v: unknown, allowed: readonly T[], fallback: T): T =>
  typeof v === 'string' && (allowed as readonly string[]).includes(v.trim().toLowerCase()) ? (v.trim().toLowerCase() as T) : fallback
const without = (o: Json, keys: string[]): Json => Object.fromEntries(Object.entries(o).filter(([k]) => !keys.includes(k)))

const SIZES: PictureSize[] = ['none', 'small', 'medium', 'large']

/** The most text columns a section of this width may have. */
export const mostColumns = (width: Width): number => (width === 'third' ? 1 : width === 'half' ? 2 : 4)

function readSection(raw: unknown): PaperSection {
  const s = isObject(raw) ? raw : {}
  return {
    name: text(s.name),
    about: text(s.about),
    feeds: Array.isArray(s.feeds) ? s.feeds.filter(isObject).map((f) => ({ name: text(f.name), url: text(f.url) })) : [],
    only: text(s.only),
    focus: texts(s.focus),
    keywords: texts(s.keywords),
    style: text(s.style),
    width: oneOf(s.width, ['full', 'half', 'third'], 'full'),
    columns: whole(s.columns, 1, 4),
    picture: typeof s.picture === 'string' && SIZES.includes(s.picture as PictureSize) ? (s.picture as PictureSize) : null,
    pictureAt: oneOf(s.pictureAt, ['beside', 'above'], 'beside'),
    articles: whole(s.articles, 1, 10),
    length: oneOf(s.length, ['short', 'normal', 'long'], 'normal'),
    onlyWithKeywords: s.onlyWithKeywords === true,
    hoursBack: whole(s.hoursBack, 1, 168),
    form: oneOf(s.form, ['articles', 'briefs', 'feature', 'explainer'], 'articles'),
    rest: without(s, SECTION_KEYS),
  }
}

/** The paper in these settings, or undefined while they are not a JSON object. */
export function readPaper(settings: string): Paper | undefined {
  let parsed: unknown
  try {
    parsed = JSON.parse(settings)
  } catch {
    return undefined
  }
  if (!isObject(parsed)) return undefined
  const layout = isObject(parsed.layout) ? parsed.layout : {}
  return {
    title: text(parsed.title, 'Morning paper'),
    subtitle: text(parsed.subtitle),
    language: text(parsed.language, 'English'),
    model: text(parsed.model, 'local/gemma'),
    folder: text(parsed.folder),
    channel: text(parsed.channel),
    reader: text(parsed.reader),
    rules: texts(parsed.rules),
    priorities: texts(parsed.priorities),
    avoid: texts(parsed.avoid),
    hoursBack: whole(parsed.hoursBack, 1, 168) ?? 24,
    perSection: whole(parsed.perSection, 1, 10) ?? 4,
    goodNews: texts(parsed.goodNews),
    layout: {
      columns: whole(layout.columns, 1, 4) ?? 2,
      frontPicture: oneOf(layout.frontPicture, SIZES, 'medium'),
      frontPictureAt: oneOf(layout.frontPictureAt, ['beside', 'above'], 'above'),
      pictures: oneOf(layout.pictures, SIZES, 'medium'),
      adapt: layout.adapt === true,
    },
    neverWrite: texts(parsed.neverWrite),
    proofread: parsed.proofread === true,
    sections: Array.isArray(parsed.sections) ? parsed.sections.map(readSection) : [],
    rest: without(parsed, PAPER_KEYS),
  }
}

function writeSection(s: PaperSection): Json {
  const out: Json = {
    ...s.rest,
    name: s.name,
    about: s.about,
    only: s.only,
    focus: s.focus,
    keywords: s.keywords,
    style: s.style,
    width: s.width,
    pictureAt: s.pictureAt,
    length: s.length,
    feeds: s.feeds,
  }
  if (s.columns !== null) out.columns = s.columns
  if (s.picture !== null) out.picture = s.picture
  if (s.articles !== null) out.articles = s.articles
  if (s.onlyWithKeywords) out.onlyWithKeywords = true
  if (s.hoursBack !== null) out.hoursBack = s.hoursBack
  if (s.form !== 'articles') out.form = s.form
  return out
}

/** The settings JSON of this paper, with every key it did not know as it was. */
export function writePaper(p: Paper): string {
  const { rest, sections, ...known } = p
  return JSON.stringify({ ...rest, ...known, sections: sections.map(writeSection) }, null, 2)
}

/** Lines of a text box as a list: one per line, blank lines left out. */
export const linesOf = (value: string): string[] =>
  value
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean)

/** Words typed with commas, as a list. */
export const wordsOf = (value: string): string[] =>
  value
    .split(',')
    .map((w) => w.trim())
    .filter(Boolean)

export function newSection(taken: string[]): PaperSection {
  let name = 'New section'
  for (let n = 2; taken.includes(name); n++) name = `New section ${n}`
  return readSection({ name, about: '', feeds: [{ name: '', url: '' }] })
}

/** The list with one item moved up (-1) or down (+1); unchanged at the ends. */
export function move<T>(list: T[], index: number, by: -1 | 1): T[] {
  const to = index + by
  if (to < 0 || to >= list.length) return list
  const next = [...list]
  ;[next[index], next[to]] = [next[to], next[index]]
  return next
}

/** The paper with a section renamed, the good news following its new name. */
export function renameSection(p: Paper, index: number, name: string): Paper {
  const old = p.sections[index].name
  return {
    ...p,
    sections: p.sections.map((s, i) => (i === index ? { ...s, name } : s)),
    goodNews: p.goodNews.map((g) => (g === old ? name : g)),
  }
}

/** The paper without a section, nor its name among the good news. */
export function removeSection(p: Paper, index: number): Paper {
  const old = p.sections[index].name
  return {
    ...p,
    sections: p.sections.filter((_, i) => i !== index),
    goodNews: p.goodNews.filter((g) => g !== old),
  }
}

const isWebAddress = (url: string): boolean => {
  try {
    const u = new URL(url)
    return u.protocol === 'https:' || u.protocol === 'http:'
  } catch {
    return false
  }
}

/** What keeps the paper from being saved, in words for the person; empty when nothing does. */
export function paperProblems(p: Paper): string[] {
  const problems: string[] = []
  if (p.sections.length === 0) problems.push('The paper needs at least one section.')
  const names = p.sections.map((s) => s.name.trim().toLowerCase())
  p.sections.forEach((s, i) => {
    const called = s.name.trim() || `Section ${i + 1}`
    if (!s.name.trim()) problems.push(`Section ${i + 1} needs a name.`)
    else if (names.indexOf(names[i]) !== i) problems.push(`Two sections are called ${s.name.trim()}.`)
    const feeds = s.feeds.filter((f) => f.url.trim())
    if (feeds.length === 0) problems.push(`${called} needs at least one source.`)
    if (feeds.some((f) => !isWebAddress(f.url.trim()))) problems.push(`${called} has a source that is not a web address.`)
    if (s.onlyWithKeywords && s.keywords.length === 0) problems.push(`${called} prints only news naming its words, but has none.`)
  })
  return problems
}

/** The paper ready to save: sources without an address left out, names and addresses trimmed. */
export function tidyPaper(p: Paper): Paper {
  return {
    ...p,
    sections: p.sections.map((s) => ({
      ...s,
      name: s.name.trim(),
      feeds: s.feeds
        .filter((f) => f.url.trim())
        .map((f) => ({
          name: f.name.trim() || hostOf(f.url),
          url: f.url.trim(),
        })),
    })),
  }
}

/** The site of an address, for a source with no name. */
export function hostOf(url: string): string {
  try {
    return new URL(url.trim()).hostname.replace(/^www\./, '')
  } catch {
    return url.trim()
  }
}
