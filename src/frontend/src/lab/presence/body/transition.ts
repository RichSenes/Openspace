/**
 * The way Shiori changes: she dissolves into characters and glitches, changes, and comes back.
 * A transition is a pure function of the time since it started.
 */
export interface Change {
  /** 'skin': a new body (long); 'mode': the same body recompiling (short). */
  kind: 'skin' | 'mode'
  start: number
}

export const DURATION = { skin: 1.1, mode: 0.55 }

/** How much ASCII and glitch to show, and whether the new body is already in place. */
export function changeAt(c: Change | null, now: number, calm: boolean): { ascii: number; glitch: number; swapped: boolean; done: boolean } {
  if (!c) return { ascii: 0, glitch: 0, swapped: true, done: true }
  const d = DURATION[c.kind] * (calm ? 0.6 : 1)
  const p = (now - c.start) / d
  if (p >= 1) return { ascii: 0, glitch: 0, swapped: true, done: true }
  const peak = c.kind === 'skin' ? 1 : 0.55
  // Up fast, hold a moment, down slower.
  const up = Math.min(1, p / 0.3)
  const down = p < 0.5 ? 1 : 1 - (p - 0.5) / 0.5
  const ascii = peak * Math.min(up, down) ** 0.8
  const glitch = calm ? 0 : Math.max(0, 1 - Math.abs(p - 0.42) / 0.3) * (c.kind === 'skin' ? 1 : 0.6)
  return { ascii, glitch, swapped: p >= 0.45, done: false }
}
