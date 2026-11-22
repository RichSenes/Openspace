/** One line of a unified diff as the history shows it. */
export interface DiffLine {
  kind: 'add' | 'del' | 'same' | 'hunk' | 'meta'
  text: string
  /** The line's number in the version before; null for added lines and headers. */
  old: number | null
  /** The line's number in this version; null for removed lines and headers. */
  new: number | null
}

const HUNK = /^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@/

/**
 * A unified diff (as git writes it) as numbered lines. The file headers before the first hunk
 * become meta lines; "\ No newline at end of file" too.
 */
export function parseDiff(diff: string): DiffLine[] {
  const lines: DiffLine[] = []
  let oldAt = 0
  let newAt = 0
  let inHunk = false
  const rows = diff.replace(/\r\n/g, '\n').split('\n')
  if (rows.at(-1) === '') rows.pop()
  for (const row of rows) {
    const hunk = HUNK.exec(row)
    if (hunk) {
      oldAt = Number(hunk[1])
      newAt = Number(hunk[2])
      inHunk = true
      lines.push({ kind: 'hunk', text: row, old: null, new: null })
    } else if (!inHunk || row.startsWith('\\')) {
      lines.push({ kind: 'meta', text: row, old: null, new: null })
    } else if (row.startsWith('+')) {
      lines.push({ kind: 'add', text: row.slice(1), old: null, new: newAt++ })
    } else if (row.startsWith('-')) {
      lines.push({ kind: 'del', text: row.slice(1), old: oldAt++, new: null })
    } else {
      lines.push({ kind: 'same', text: row.slice(1), old: oldAt++, new: newAt++ })
    }
  }
  return lines
}

/** How many lines a diff adds and removes. */
export function diffStats(lines: DiffLine[]): { added: number; removed: number } {
  return {
    added: lines.filter((l) => l.kind === 'add').length,
    removed: lines.filter((l) => l.kind === 'del').length,
  }
}

/** Only the hunks, without git's file headers: what a person wants to read. */
export const withoutHeaders = (lines: DiffLine[]) => lines.filter((l) => l.kind !== 'meta' || l.text.startsWith('\\'))

/** A commit's short form, as git shows it. */
export const shortCommit = (commit: string) => commit.slice(0, 7)
