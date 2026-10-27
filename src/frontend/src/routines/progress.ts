/** After this long without a word, a running routine may be stuck: long steps speak at every item, and waits once a minute. */
export const QUIET_MS = 10 * 60_000

/** "40 s", "17 min", "2 h 5 min". */
export function span(ms: number): string {
  const seconds = Math.max(0, Math.round(ms / 1000))
  if (seconds < 60) return `${seconds} s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes} min`
  return `${Math.floor(minutes / 60)} h${minutes % 60 ? ` ${minutes % 60} min` : ''}`
}

/** How a running routine is doing, in words: since when it runs, when it last spoke, and whether it went quiet too long. */
export function runNews(startedAt: string | undefined, heardAt: number | undefined, now: number): { since: string; quiet: string | undefined } {
  const started = startedAt ? `Started ${span(now - new Date(startedAt).getTime())} ago` : 'Starting'
  if (heardAt === undefined) return { since: `${started}; its next update shows up here.`, quiet: undefined }
  const silence = now - heardAt
  return {
    since: `${started}, last update ${span(silence)} ago.`,
    quiet: silence >= QUIET_MS ? `No update for ${span(silence)}: it may be stuck. Stop it and run it again, or look at its History.` : undefined,
  }
}
