import { describe, expect, it } from 'vitest'
import { QUIET_MS, runNews, span } from './progress'

describe('span', () => {
  it('says a time span the short way', () => {
    expect(span(40_000)).toBe('40 s')
    expect(span(17 * 60_000 + 20_000)).toBe('17 min')
    expect(span(125 * 60_000)).toBe('2 h 5 min')
    expect(span(120 * 60_000)).toBe('2 h')
  })
})

describe('runNews', () => {
  const start = '2026-10-27T22:00:00Z'
  const at = (minutes: number) => new Date(start).getTime() + minutes * 60_000

  it('says since when it runs and when it last spoke', () => {
    expect(runNews(start, at(16), at(17))).toEqual({ since: 'Started 17 min ago, last update 1 min ago.', quiet: undefined })
  })

  it('waits for the next update when the page has heard nothing yet', () => {
    expect(runNews(start, undefined, at(3)).since).toBe('Started 3 min ago; its next update shows up here.')
  })

  it('warns when a run went quiet for too long', () => {
    expect(runNews(start, at(5), at(5) + QUIET_MS).quiet).toBe('No update for 10 min: it may be stuck. Stop it and run it again, or look at its History.')
  })
})
