import type { OfficeSetup } from '../api/types'

/** An office file is a few kilobytes; anything far larger is not one. */
const MAX_FILE = 2_000_000

/** The office file's text, or what is wrong with it: checked before it is sent, the API checks it again. */
export function readSetupFile(text: string): { setup: OfficeSetup } | { problem: string } {
  if (text.length > MAX_FILE) return { problem: 'This file is too large to be an office file.' }
  let parsed: unknown
  try {
    parsed = JSON.parse(text)
  } catch {
    return { problem: 'This file is not valid JSON: choose a file exported from an office.' }
  }
  if (typeof parsed !== 'object' || parsed === null || (parsed as { format?: unknown }).format !== 'openspace-office') {
    return { problem: 'This is not an office file made by Openspace.' }
  }
  const setup = parsed as OfficeSetup
  if (typeof setup.version !== 'number' || setup.version > 1) {
    return { problem: 'This office file was made by a newer Openspace: update this one first.' }
  }
  return { setup }
}

/** What the file holds, in one line, to show before it is opened. */
export function describeSetup(setup: OfficeSetup): string {
  const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`
  const rooms = setup.rooms ?? []
  const desks = rooms.reduce((sum, r) => sum + (r.desks?.length ?? 0), 0)
  return [count(rooms.length, 'room', 'rooms'), count(desks, 'desk', 'desks'), count(setup.roles?.length ?? 0, 'role', 'roles'), count(setup.routines?.length ?? 0, 'routine', 'routines')].join(', ')
}

/** The name the exported file gets: the office's, in plain letters. */
export function setupFileName(officeName: string): string {
  const slug = officeName
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  return `${slug || 'office'}.openspace.json`
}
