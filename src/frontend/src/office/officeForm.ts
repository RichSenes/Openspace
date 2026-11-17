import type { ModelOption, OfficeView } from '../api/types'
import { CLAUDE_RESERVE } from './reserve'

/** The office's own settings, as the Office page edits them before one save. */
export interface OfficeForm {
  name: string
  repositoryPath: string
  localOnly: boolean
  /** One absolute path per line, as typed. */
  privatePaths: string
  reserveModels: string[]
}

export type OfficePart = 'name' | 'repository' | 'localOnly' | 'privatePaths' | 'reserveModels'

export function formOf(office: OfficeView): OfficeForm {
  return {
    name: office.name,
    repositoryPath: office.repositoryPath ?? '',
    localOnly: Boolean(office.localOnly),
    privatePaths: (office.privatePaths ?? []).join('\n'),
    reserveModels: [...(office.reserveModels ?? [])],
  }
}

/** The private folders typed: one per line, blanks left out. */
export function pathsOf(text: string): string[] {
  return text
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean)
}

const same = (a: string[], b: string[]) => a.length === b.length && a.every((x, i) => x === b[i])

/** What the form changes, in the order it is saved: the repository first, since it may be refused. */
export function changedParts(office: OfficeView, form: OfficeForm): OfficePart[] {
  const saved = formOf(office)
  const parts: OfficePart[] = []
  if (form.repositoryPath.trim() !== saved.repositoryPath) parts.push('repository')
  if (form.name.trim() !== saved.name) parts.push('name')
  if (form.localOnly !== saved.localOnly) parts.push('localOnly')
  if (!same(pathsOf(form.privatePaths), pathsOf(saved.privatePaths))) parts.push('privatePaths')
  if (!same(form.reserveModels, saved.reserveModels)) parts.push('reserveModels')
  return parts
}

/** Moves one item of a list up (-1) or down (+1), keeping the rest in order. */
export function move<T>(list: T[], index: number, by: -1 | 1): T[] {
  const to = index + by
  if (to < 0 || to >= list.length) return list
  const next = [...list]
  ;[next[index], next[to]] = [next[to], next[index]]
  return next
}

/** The models a reserve can be: OpenCode's as listed, Claude Code's as claude-code/<model>. */
export function reserveChoices(openCode: ModelOption[] | undefined, claude: ModelOption[] | undefined): ModelOption[] {
  return [
    ...(claude ?? []).map((m) => ({ ...m, id: `${CLAUDE_RESERVE}${m.id}`, name: `Claude Code · ${m.name}` })),
    ...(openCode ?? []),
  ]
}
