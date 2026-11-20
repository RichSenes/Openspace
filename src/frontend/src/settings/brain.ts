/** The kinds of notes in the order a person thinks of them, with their names in the page. */
export const KIND_LABEL: Record<string, string> = {
  rule: 'Rules',
  decision: 'Decisions',
  plan: 'Plans',
  study: 'Studies',
  session: 'Sessions',
  digest: 'Digests',
  journal: 'Journals',
  note: 'Other notes',
}

/** Each kind with its count, known kinds first in their order, then any others by name. */
export function kindRows(kinds: Record<string, number>): { kind: string; label: string; count: number }[] {
  const known = Object.keys(KIND_LABEL)
  const order = (k: string) => (known.includes(k) ? known.indexOf(k) : known.length)
  return Object.entries(kinds)
    .sort(([a], [b]) => order(a) - order(b) || a.localeCompare(b))
    .map(([kind, count]) => ({ kind, label: KIND_LABEL[kind] ?? kind, count }))
}

/** The link that opens the vault in Obsidian, which adds it as a vault the first time. */
export const obsidianLink = (folder: string) => `obsidian://open?path=${encodeURIComponent(folder)}`
