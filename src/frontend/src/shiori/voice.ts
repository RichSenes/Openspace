/**
 * Talking to Shiori: hold to talk (the mic button, or the space bar when the composer is empty
 * and not focused), let go, and what was said becomes the next message. Recording happens in the
 * browser; the words are found on this machine by the listening server, never online.
 */
export type Ears = 'idle' | 'asking' | 'recording' | 'hearing' | 'denied' | 'unavailable'

export interface Heard {
  text: string
  language: string
  seconds: number
}

/** Recordings shorter than this are a slip of the finger, not speech. */
export const MIN_MS = 400
/** A long monologue is cut here, so a stuck button never records forever. */
export const MAX_MS = 60_000

/** What the person sees beside the mic, by state. */
export function earsLabel(ears: Ears, ms: number): string {
  switch (ears) {
    case 'asking':
      return 'Allow the microphone in the browser'
    case 'recording':
      return `Listening… ${formatDuration(ms)} · let go to send`
    case 'hearing':
      return 'Shiori is hearing you…'
    case 'denied':
      return 'The microphone is blocked for this page: allow it in the browser’s site settings'
    case 'unavailable':
      return 'This browser cannot record here'
    default:
      return 'Hold to talk, or hold the space bar'
  }
}

export function formatDuration(ms: number): string {
  const s = Math.floor(ms / 1000)
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
}

/** The recording format the browser can make, preferring opus in webm, which ffmpeg reads. */
export function pickMime(isSupported: (mime: string) => boolean): string | undefined {
  return ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus', 'audio/mp4'].find(isSupported)
}

/** What to do with what was heard: send it, put it in the composer for a look, or nothing. */
export function afterHearing(heard: Heard, typed: string): { send: string | null; compose: string } {
  const words = heard.text.trim()
  if (!words) return { send: null, compose: typed }
  // Something already typed is kept: the spoken part is added, and the whole waits to be sent.
  if (typed.trim()) return { send: null, compose: `${typed.trimEnd()} ${words}` }
  return { send: words, compose: '' }
}

/** Whether a key press starts talking: space, not repeated, and not while writing. */
export function isTalkKey(e: { code: string; repeat: boolean; target: EventTarget | null }, composing: boolean): boolean {
  if (e.code !== 'Space' || e.repeat || composing) return false
  const t = e.target as HTMLElement | null
  const tag = t?.tagName
  return !(tag === 'TEXTAREA' || tag === 'INPUT' || tag === 'SELECT' || tag === 'BUTTON' || t?.isContentEditable)
}

/** The microphone's loudness, 0..1, from a byte time-domain frame: drives Shiori's voice reaction. */
export function loudness(frame: Uint8Array): number {
  let sum = 0
  for (const v of frame) {
    const x = (v - 128) / 128
    sum += x * x
  }
  return Math.min(1, Math.sqrt(sum / Math.max(1, frame.length)) * 4)
}
