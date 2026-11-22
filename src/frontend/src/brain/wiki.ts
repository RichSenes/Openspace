import { Marked, type Tokens } from 'marked'
import type { TreeFolder, TreeNote } from './types'

/** Openspace rewrites this note from the running tasks every few seconds: nobody edits it. */
export const NOW_PATH = 'Now.md'
/** The person's rules: only they change it, agents can only propose. */
export const CONSTITUTION_PATH = 'Constitution.md'

export const isNow = (path: string) => path.toLowerCase() === NOW_PATH.toLowerCase()
export const isConstitution = (path: string) => path.toLowerCase() === CONSTITUTION_PATH.toLowerCase()

/** Every note of the tree, in the tree's own order (folders first, then notes). */
export function flattenTree(folder: TreeFolder): TreeNote[] {
  return [...folder.folders.flatMap(flattenTree), ...folder.notes]
}

/** The kinds the notes carry, sorted, without blanks. */
export function kindsOf(notes: { kind: string | null }[]): string[] {
  return [...new Set(notes.map((n) => n.kind).filter((k): k is string => !!k))].sort()
}

/**
 * The tree with only the notes of a kind; folders left empty go away. With no kind, the tree
 * as it is. The root always stays, even empty.
 */
export function filterTree(folder: TreeFolder, kind: string | undefined): TreeFolder {
  if (!kind) return folder
  const prune = (f: TreeFolder): TreeFolder => ({
    ...f,
    folders: f.folders.map(prune).filter((x) => x.folders.length > 0 || x.notes.length > 0),
    notes: f.notes.filter((n) => n.kind === kind),
  })
  return prune(folder)
}

export function countNotes(folder: TreeFolder): number {
  return folder.notes.length + folder.folders.reduce((sum, f) => sum + countNotes(f), 0)
}

/** The folders a note sits in, outermost first: "a/b/c.md" is inside "a" and "a/b". */
export function foldersOf(path: string): string[] {
  const parts = path.split('/').slice(0, -1)
  return parts.map((_, i) => parts.slice(0, i + 1).join('/'))
}

/** The note's first folder, or "" for notes at the root of the vault. */
export const topFolder = (path: string) => (path.includes('/') ? path.slice(0, path.indexOf('/')) : '')

const folderOf = (path: string) => (path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '')

/**
 * The note a `[[link]]` points at, the way the server and Obsidian resolve it: the whole path,
 * the end of it, or the file's name, with or without `.md`, in any case. When names clash the
 * note beside the link wins, then the shortest path. Null when no note answers.
 */
export function resolveLink(link: string, from: string, paths: readonly string[]): string | null {
  let target = link.trim().replace(/\\/g, '/').replace(/^\/+/, '')
  const hash = target.indexOf('#')
  if (hash >= 0) target = target.slice(0, hash)
  if (!target) return null
  if (!target.toLowerCase().endsWith('.md')) target += '.md'
  if (paths.includes(target)) return target
  const lower = target.toLowerCase()
  const name = lower.slice(lower.lastIndexOf('/') + 1)
  const here = folderOf(from).toLowerCase()
  const fits = paths.filter((p) => {
    const q = p.toLowerCase()
    return q.slice(q.lastIndexOf('/') + 1) === name && (q === lower || q.endsWith('/' + lower))
  })
  fits.sort(
    (a, b) =>
      Number(folderOf(b).toLowerCase() === here) - Number(folderOf(a).toLowerCase() === here) ||
      a.length - b.length ||
      (a < b ? -1 : a > b ? 1 : 0),
  )
  return fits[0] ?? null
}

/** A `[[target#heading|label]]` taken apart; null when the text does not start with one. */
export function parseWikiLink(text: string): { raw: string; target: string; label: string } | null {
  const match = /^\[\[([^\]|\n]+?)(?:\|([^\]\n]+))?\]\]/.exec(text)
  if (!match) return null
  const target = match[1].trim()
  const label = (match[2] ?? target.replace(/#.*$/, '').replace(/\.md$/i, '').split('/').pop() ?? target).trim()
  return { raw: match[0], target, label: label || target }
}

/** Every link target written in a Markdown text, in order, outside code. */
export function linksIn(markdown: string): string[] {
  const found: string[] = []
  const lexer = new Marked({ extensions: [wikiExtension(() => null)] })
  lexer.walkTokens(lexer.lexer(markdown), (token) => {
    if (token.type === 'wikilink') found.push((token as WikiToken).target)
  })
  return found
}

interface WikiToken extends Tokens.Generic {
  type: 'wikilink'
  raw: string
  target: string
  label: string
}

const escape = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

function wikiExtension(resolve: (target: string) => string | null) {
  return {
    name: 'wikilink',
    level: 'inline' as const,
    start: (src: string) => {
      const at = src.indexOf('[[')
      return at < 0 ? undefined : at
    },
    tokenizer(src: string): WikiToken | undefined {
      const link = parseWikiLink(src)
      return link ? { type: 'wikilink', ...link } : undefined
    },
    renderer(token: Tokens.Generic) {
      const { target, label } = token as WikiToken
      const path = resolve(target)
      return path
        ? `<a href="#" class="wikilink" data-path="${escape(path)}">${escape(label)}</a>`
        : `<span class="wikilink wikilink-missing" title="No note called ${escape(target)}">${escape(label)}</span>`
    },
  }
}

/**
 * The note's Markdown as HTML, with each `[[link]]` a link that carries the path of the note
 * it resolves to, or a muted word when no note answers. Code keeps its brackets. The caller
 * sanitises the result before it reaches the page.
 */
export function renderNote(markdown: string, resolve: (target: string) => string | null): string {
  const marked = new Marked({ extensions: [wikiExtension(resolve)] })
  return marked.parse(markdown, { async: false })
}

export type ReviewState = 'overdue' | 'soon' | 'later'

/** When a review date falls: past or today, within two weeks, or later. */
export function reviewState(review: string, today: Date = new Date()): ReviewState {
  const [y, m, d] = review.split('-').map(Number)
  const due = Date.UTC(y, m - 1, d)
  const now = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())
  const days = Math.round((due - now) / 86_400_000)
  return days <= 0 ? 'overdue' : days <= 14 ? 'soon' : 'later'
}

/** The words for a note's status badge: known statuses get their own colour class. */
export function statusClass(status: string | null | undefined): string {
  switch (status?.toLowerCase()) {
    case 'accepted':
    case 'proposed':
    case 'superseded':
    case 'rejected':
      return `brain-status-${status.toLowerCase()}`
    default:
      return 'brain-status-other'
  }
}

/** Why a note cannot be edited here, or null when it can. */
export function editRefusal(path: string): string | null {
  return isNow(path) ? 'Openspace writes Now.md from the running tasks, so it is read only.' : null
}

/** The body without its first heading when that heading is the title already shown above it. */
export function withoutTitle(body: string, title: string): string {
  const match = /^\s*#\s+(.+?)\s*#*\s*(?:\n|$)/.exec(body)
  return match && match[1].trim() === title.trim() ? body.slice(match[0].length).replace(/^\s*\n/, '') : body
}
