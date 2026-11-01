import type { Release, VersionView } from '../api/types'

/** A release put off until a moment: "remind me tomorrow". */
export interface Snooze {
  version: string
  until: string
}

/** How long "remind me tomorrow" puts a release off. */
export const SNOOZE_MS = 24 * 60 * 60_000

/** The release to tell about now: newer than this one, and not put off until later. */
export function releaseToTell(view: VersionView | undefined, snooze: Snooze | null, now: Date): Release | undefined {
  if (!view?.newer || !view.latest) return undefined
  if (snooze && snooze.version === view.latest.version && new Date(snooze.until) > now) return undefined
  return view.latest
}

export function snoozed(release: Release, now: Date): Snooze {
  return { version: release.version, until: new Date(now.getTime() + SNOOZE_MS).toISOString() }
}

/** Reads a stored snooze; anything else is none. */
export function readSnooze(text: string | null): Snooze | null {
  if (!text) return null
  try {
    const value = JSON.parse(text) as Partial<Snooze>
    return typeof value.version === 'string' && typeof value.until === 'string' ? { version: value.version, until: value.until } : null
  } catch {
    return null
  }
}

export type NoteLine = { kind: 'heading' | 'item' | 'text'; text: string }

/**
 * Release notes as plain lines: headings, list items and text, with Markdown's marks
 * taken out (bold, code, links keep their words). Nothing is rendered as HTML.
 */
export function noteLines(markdown: string): NoteLine[] {
  const plain = (s: string) =>
    s
      .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
      .replace(/[*_`]+/g, '')
      .trim()
  return markdown
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line): NoteLine => {
      if (/^#{1,6}\s/.test(line)) return { kind: 'heading', text: plain(line.replace(/^#+\s*/, '')) }
      if (/^[-*+]\s/.test(line)) return { kind: 'item', text: plain(line.replace(/^[-*+]\s*/, '')) }
      return { kind: 'text', text: plain(line) }
    })
}
