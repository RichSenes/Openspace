import { useEffect, useState, type RefObject } from 'react'

/** What the routine editors share that is not drawn: languages, the index that follows the page, small helpers. */

export const LANGUAGES = ['English', 'Italian', 'Japanese', 'Spanish', 'French', 'German', 'Portuguese']

/** The index follows the part in view: the last one whose top has reached the upper third. */
export function useFollowIndex(body: RefObject<HTMLDivElement | null>, ready: boolean, onPart: (id: string) => void) {
  useEffect(() => {
    const root = body.current
    const scroller = root?.closest('.main')
    if (!root || !scroller) return
    const follow = () => {
      const line = scroller.getBoundingClientRect().top + scroller.clientHeight / 3
      const parts = [...root.querySelectorAll<HTMLElement>('[id^="paper-part-"]')]
      const current = parts.filter((el) => el.getBoundingClientRect().top <= line).pop() ?? parts[0]
      if (current) onPart(current.id.replace('paper-part-', ''))
    }
    scroller.addEventListener('scroll', follow, { passive: true })
    return () => scroller.removeEventListener('scroll', follow)
  }, [body, ready, onPart])
}

export function clamp(text: string, min: number, max: number, fallback: number): number {
  const n = Number(text)
  return Number.isInteger(n) ? Math.min(max, Math.max(min, n)) : fallback
}

export function useDebounced<T>(value: T, ms: number): T {
  const [settled, setSettled] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), ms)
    return () => clearTimeout(timer)
  }, [value, ms])
  return settled
}
