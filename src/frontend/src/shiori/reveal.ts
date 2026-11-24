import { useEffect, useState } from 'react'

/**
 * Her answer is shown at the pace it is read, not all at once: a voice chat is followed by
 * reading along, and a wall of text arriving in a second is not a reply. A newer answer starts
 * from its first word; one already there when the page opens is shown whole; skip shows it all.
 */
export const READING_CPS = 18 // about 200 words a minute, an easy pace

/** How many characters are shown after `ms` more, never past what has arrived. */
export function revealed(shown: number, arrived: number, ms: number): number {
  return Math.min(arrived, shown + (ms * READING_CPS) / 1000)
}

/**
 * `turn` names the exchange (the person's last message): a new one starts the answer over.
 * `follow`, while her voice speaks the answer, is where in the text it is: the words appear as
 * they are said, instead of at reading pace (which takes over again if her voice stops).
 */
export function useReveal(turn: string | null, arrived: number, follow: number | null = null) {
  const [state, setState] = useState({ turn, shown: Number.POSITIVE_INFINITY, skipped: true })
  if (state.turn !== turn) setState({ turn, shown: 0, skipped: false })
  const following = follow !== null && !state.skipped && state.turn === turn
  // Remembered, so reading pace goes on from the voice's place if the voice stops.
  if (following && state.shown !== Math.min(arrived, follow)) setState({ ...state, shown: Math.min(arrived, follow) })

  const behind = !state.skipped && state.shown < arrived
  useEffect(() => {
    if (!behind || following) return
    let last = performance.now()
    let frame = requestAnimationFrame(function step(now) {
      const ms = now - last
      last = now
      setState((s) => (s.turn === turn ? { ...s, shown: revealed(s.shown, arrived, ms) } : s))
      frame = requestAnimationFrame(step)
    })
    return () => cancelAnimationFrame(frame)
  }, [behind, following, turn, arrived])

  return {
    shown: state.skipped ? arrived : Math.floor(state.shown),
    /** Still writing what has arrived: the skip button shows. */
    behind,
    skip: () => setState((s) => ({ ...s, skipped: true })),
  }
}
