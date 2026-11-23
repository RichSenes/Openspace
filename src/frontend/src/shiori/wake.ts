/**
 * Calling Shiori by name: while always listening is on, the page keeps the microphone open,
 * cuts what is said into sentences on its own, and asks the listening server on this machine
 * whether a sentence starts with her name. Only then is it sent as a message; everything
 * else is dropped where it was heard.
 */
export type Wake = 'off' | 'starting' | 'waiting' | 'called' | 'hearing' | 'denied' | 'unavailable'

/** The rate the listening server works at; sentences are sent as 16 kHz mono WAV. */
export const RATE = 16_000
/** After only her name ("Shiori?"), this long to say what you need. */
export const CALLED_MS = 8_000

const FRAME = 320 // 20 ms
const PRE_ROLL = 15 // 300 ms kept from before the voice started, so the first sound is not cut
const START = 3 // 60 ms of voice to begin a sentence
const END = 35 // 700 ms of quiet to end it
const SHORTEST = 15 // 300 ms of voice: less is a cough or a click
const LONGEST = 600 // 12 s: a long talk in the room is not a request

export function wakeLabel(wake: Wake): string {
  switch (wake) {
    case 'starting':
      return 'Allow the microphone in the browser'
    case 'waiting':
      return 'Say “Shiori…” to talk to her, or hold the mic'
    case 'called':
      return 'Shiori is listening: say what you need'
    case 'hearing':
      return 'Shiori is hearing you…'
    case 'denied':
      return 'The microphone is blocked for this page: allow it in the browser’s site settings'
    case 'unavailable':
      return 'This browser cannot listen here'
    default:
      return ''
  }
}

/** Linear resampling to the server's rate; the browser records at 44.1 or 48 kHz. */
export function resample(input: Float32Array, from: number, to = RATE): Float32Array {
  if (from === to) return input
  const out = new Float32Array(Math.floor((input.length * to) / from))
  const step = from / to
  for (let i = 0; i < out.length; i++) {
    const at = i * step
    const j = Math.floor(at)
    const next = input[Math.min(j + 1, input.length - 1)]
    out[i] = input[j] + (next - input[j]) * (at - j)
  }
  return out
}

/** 16-bit PCM WAV, which the listening server reads through ffmpeg. */
export function toWav(samples: Float32Array, rate = RATE): ArrayBuffer {
  const buffer = new ArrayBuffer(44 + samples.length * 2)
  const v = new DataView(buffer)
  const text = (at: number, s: string) => [...s].forEach((c, i) => v.setUint8(at + i, c.charCodeAt(0)))
  text(0, 'RIFF')
  v.setUint32(4, 36 + samples.length * 2, true)
  text(8, 'WAVE')
  text(12, 'fmt ')
  v.setUint32(16, 16, true)
  v.setUint16(20, 1, true) // PCM
  v.setUint16(22, 1, true) // mono
  v.setUint32(24, rate, true)
  v.setUint32(28, rate * 2, true)
  v.setUint16(32, 2, true)
  v.setUint16(34, 16, true)
  text(36, 'data')
  v.setUint32(40, samples.length * 2, true)
  samples.forEach((s, i) => v.setInt16(44 + i * 2, Math.max(-1, Math.min(1, s)) * 0x7fff, true))
  return buffer
}

function rms(frame: Float32Array): number {
  let sum = 0
  for (const x of frame) sum += x * x
  return Math.sqrt(sum / frame.length)
}

/**
 * Cuts a stream of 16 kHz samples into sentences: voice louder than the room starts one,
 * a pause ends it. The room's own noise is learnt while nobody talks.
 */
export class Utterances {
  private rest = new Float32Array(0)
  private before: Float32Array[] = []
  private sentence: Float32Array[] = []
  private loud = 0
  private quiet = 0
  private spoken = 0
  private floor = 0.004

  /** Feeds samples; returns the sentences that ended in them. */
  push(chunk: Float32Array): Float32Array[] {
    const all = new Float32Array(this.rest.length + chunk.length)
    all.set(this.rest)
    all.set(chunk, this.rest.length)
    const done: Float32Array[] = []
    let at = 0
    for (; at + FRAME <= all.length; at += FRAME) {
      const sentence = this.frame(all.slice(at, at + FRAME))
      if (sentence) done.push(sentence)
    }
    this.rest = all.slice(at)
    return done
  }

  /** Forgets anything half heard (while holding the mic, or after a pause). */
  reset() {
    this.before = []
    this.sentence = []
    this.loud = 0
    this.quiet = 0
    this.spoken = 0
  }

  private frame(frame: Float32Array): Float32Array | null {
    const level = rms(frame)
    const voiced = level > Math.max(0.012, this.floor * 3)
    if (!this.sentence.length) {
      this.floor = voiced ? this.floor : this.floor * 0.95 + level * 0.05
      this.before.push(frame)
      this.loud = voiced ? this.loud + 1 : 0
      if (this.loud >= START) {
        this.sentence = this.before
        this.before = []
        this.quiet = 0
        this.spoken = this.loud
      } else if (this.before.length > PRE_ROLL) this.before.shift()
      return null
    }
    this.sentence.push(frame)
    this.quiet = voiced ? 0 : this.quiet + 1
    if (voiced) this.spoken++
    if (this.quiet < END && this.sentence.length < LONGEST) return null
    const frames = this.sentence
    const spoken = this.spoken
    this.reset()
    if (spoken < SHORTEST) return null
    const out = new Float32Array(frames.length * FRAME)
    frames.forEach((f, i) => out.set(f, i * FRAME))
    return out
  }
}

const BY_NAME_KEY = 'openspace.shiori.byName'

/** Whether always listening was left on: it is turned on by the person, never by itself. */
export function loadByName(): boolean {
  try {
    return localStorage.getItem(BY_NAME_KEY) === '1'
  } catch {
    return false
  }
}

export function saveByName(on: boolean) {
  try {
    localStorage.setItem(BY_NAME_KEY, on ? '1' : '0')
  } catch {
    // Private windows may refuse storage: the choice still holds for this visit.
  }
}
