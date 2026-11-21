import type { Entry, Token } from '../shiori/script'

type ShioriEntry = Extract<Entry, { kind: 'shiori' }>
type UserEntry = Extract<Entry, { kind: 'user' }>

/** What the subtitles under Shiori show: the last thing you said and her answer so far. */
export interface Subtitles {
  you: UserEntry | null
  shiori: ShioriEntry | null
  /** The answer's tokens shown so far. */
  shown: Token[]
  /** The answer is long enough to need a card instead of a subtitle. */
  long: boolean
  /** Notes the HUD pins on her body now: what she is reading, or what she has cited. */
  notes: string[]
}

/** Characters past which an answer is read as a card and Shiori steps back to make room. */
export const LONG_ANSWER = 320

export function textOf(tokens: Token[]): string {
  return tokens.map((t, i) => (i > 0 && !t.glued ? ' ' : '') + (t.type === 'cite' ? t.path : t.text)).join('')
}

export function subtitles(entries: Entry[]): Subtitles {
  let you: UserEntry | null = null
  let shiori: ShioriEntry | null = null
  for (let i = entries.length - 1; i >= 0; i--) {
    const e = entries[i]
    if (!shiori && e.kind === 'shiori') shiori = e
    if (!you && e.kind === 'user') you = e
    if (you && shiori) break
  }
  // An answer belongs under the question it answers: a newer question hides the older answer.
  if (shiori && you && you.at > shiori.at) shiori = null
  const shown = shiori ? shiori.tokens.slice(0, shiori.shown) : []
  const long = shiori ? textOf(shiori.tokens).length > LONG_ANSWER && shiori.shown > 0 : false
  let notes: string[] = []
  if (shiori?.thinking) notes = shiori.reads
  else if (shiori) notes = shown.flatMap((t) => (t.type === 'cite' ? [t.path] : []))
  return { you, shiori, shown, long, notes: [...new Set(notes)].slice(-3) }
}

/**
 * Where Shiori stands in the stage (centre as a fraction, radius in px): high and big with a
 * short subtitle, smaller and higher when a long answer needs the room.
 */
export function focusFor(w: number, h: number, long: boolean): { x: number; y: number; size: number } {
  const narrow = w < 700
  const base = narrow ? Math.min(w * 0.34, h * 0.2) : Math.min(w * 0.22, h * 0.25)
  return {
    x: 0.5,
    y: long ? (narrow ? 0.22 : 0.25) : narrow ? 0.3 : 0.37,
    size: Math.round(base * (long ? 0.66 : 1)),
  }
}
