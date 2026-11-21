/** The whiteboard beside the chat: what the session found, sorted by kind. */
export type BoardKind = 'idea' | 'doubt' | 'decision' | 'task'

export interface BoardItem {
  id: string
  kind: BoardKind
  text: string
  /** For tasks: the office that would do it. */
  office?: string
}

export const KIND_LABELS: Record<BoardKind, { one: string; many: string }> = {
  decision: { one: 'Decision', many: 'Decisions' },
  idea: { one: 'Idea', many: 'Ideas' },
  doubt: { one: 'Doubt', many: 'Doubts' },
  task: { one: 'Task', many: 'Tasks' },
}

/** The order a note reads in: what was decided first, then how we got there, then the work. */
export const KIND_ORDER: BoardKind[] = ['decision', 'idea', 'doubt', 'task']

export interface BoardSection {
  kind: BoardKind
  heading: string
  items: BoardItem[]
}

/** Items grouped by kind in note order; empty kinds are left out. */
export function boardSections(items: BoardItem[]): BoardSection[] {
  return KIND_ORDER.map((kind) => {
    const of = items.filter((i) => i.kind === kind)
    return { kind, heading: of.length === 1 ? KIND_LABELS[kind].one : KIND_LABELS[kind].many, items: of }
  }).filter((s) => s.items.length > 0)
}

export function countByKind(items: BoardItem[]): Record<BoardKind, number> {
  const out: Record<BoardKind, number> = { idea: 0, doubt: 0, decision: 0, task: 0 }
  for (const i of items) out[i.kind]++
  return out
}

export function slugify(text: string): string {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 48)
    .replace(/-+$/, '')
}

export interface SessionNote {
  path: string
  title: string
  sections: BoardSection[]
  links: string[]
  markdown: string
}

/** The note the board becomes at the end of a session, saved under Sessions/. */
export function noteFromBoard(items: BoardItem[], title: string, date: string, links: string[] = []): SessionNote {
  const sections = boardSections(items)
  const unique = [...new Set(links)]
  const lines = [`# ${title}`, '', `Session of ${date}.`, '']
  for (const s of sections) {
    lines.push(`## ${s.heading}`, '')
    for (const i of s.items) {
      const box = i.kind === 'task' ? '- [ ] ' : '- '
      lines.push(`${box}${i.text}${i.office ? ` (${i.office})` : ''}`)
    }
    lines.push('')
  }
  if (unique.length > 0) {
    lines.push('## Sources', '', ...unique.map((l) => `- [[${l}]]`), '')
  }
  return {
    path: `Sessions/${date}-${slugify(title)}.md`,
    title,
    sections,
    links: unique,
    markdown: lines.join('\n').trimEnd() + '\n',
  }
}
