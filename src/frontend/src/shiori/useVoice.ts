import { useCallback, useEffect, useRef, useState } from 'react'
import { join, Pcm } from './pcm'
import { byScript, languageOf, type Spoken } from './speech'

interface Line extends Spoken {
  language: string
}

/** A piece of a line's audio on the audio clock, as frames that are voice (true) or silence. */
export interface Piece {
  start: number
  frame: number
  voiced: boolean[]
}

/** A line being heard: its audio so far, and whether all of it has arrived. */
export interface Heard {
  line: Line
  pieces: Piece[]
  arrived: boolean
}

/** Frames of this length are voice when louder than VOICED (a pause at a comma is not). */
const FRAME = 0.02
const VOICED = 0.01

/** Which frames of some samples are voice. */
export function voicedFrames(samples: Float32Array, rate: number): boolean[] {
  const size = Math.max(1, Math.round(rate * FRAME))
  const out: boolean[] = []
  for (let i = 0; i < samples.length; i += size) {
    let sum = 0
    const end = Math.min(samples.length, i + size)
    for (let j = i; j < end; j++) sum += samples[j] * samples[j]
    out.push(Math.sqrt(sum / (end - i)) > VOICED)
  }
  return out
}

/** Seconds of voice in a line, all of it or heard by `now`. */
export function voiceIn(h: Heard, now = Number.POSITIVE_INFINITY): number {
  let seconds = 0
  for (const p of h.pieces) {
    for (let k = 0; k < p.voiced.length; k++) {
      const at = p.start + k * p.frame
      if (at >= now) return seconds
      if (p.voiced[k]) seconds += Math.min(p.frame, now - at)
    }
  }
  return seconds
}

/**
 * Where in the written text she is, given the lines heard so far and the audio clock: the words
 * move only while she is actually speaking, so they wait at her pauses (commas, full stops) as
 * she does. A line still arriving is guessed at `cps` characters per second of voice.
 */
export function placeOf(heard: Heard[], now: number, cps: number): number | null {
  if (!heard.length) return null
  let at = heard[0].line.from
  for (const h of heard) {
    if (!h.pieces.length || now < h.pieces[0].start) break
    const all = voiceIn(h)
    const lasts = h.arrived ? all : Math.max(all, h.line.text.length / cps)
    const share = lasts > 0 ? Math.min(h.arrived ? 1 : 0.95, voiceIn(h, now) / lasts) : 1
    at = h.line.from + (h.line.to - h.line.from) * share
  }
  return at
}

/** Her voice could not start because the graphics card is full: she writes instead, for now. */
class Resting extends Error {}

/** How much of her voice is gathered before it starts, and per buffer after that, in seconds. */
const LEAD = 0.35
const PIECE = 0.2

async function problemOf(response: Response): Promise<Error> {
  let detail = `Her voice could not speak (${response.status}).`
  let busy = false
  try {
    const problem = (await response.json()) as { detail?: string; title?: string; busy?: boolean }
    detail = problem.detail ?? problem.title ?? detail
    busy = !!problem.busy
  } catch {
    // Not JSON: keep the status.
  }
  return busy ? new Resting(`Her voice is resting: ${detail}. She answers in writing until there is room.`) : new Error(detail)
}

/**
 * Shiori's voice: `say` queues sentences, which are spoken in order as they are made: each one
 * streams from her voice server and starts playing after a fraction of a second, and the next
 * one is asked for as soon as the current one has arrived, so they follow without gaps. `stop`
 * silences her and drops the rest. `warm` loads her voice when a conversation starts.
 * When her voice is not running she simply stays silent; when the graphics card is full
 * (`resting`) she answers in writing only and says why.
 */
export function useVoice(onProblem: (message: string) => void) {
  const [speaking, setSpeaking] = useState(false)
  const [resting, setResting] = useState<string | null>(null)
  // Whether her voice answers, as far as the page knows: null until asked.
  const [answers, setAnswers] = useState<boolean | null>(null)
  // Where in the written text her voice is, while it speaks: the page shows the words with it.
  const [at, setAt] = useState<number | null>(null)
  const heard = useRef<Heard[]>([])
  // Characters per second of voice, learnt from each line said: guesses lines still arriving.
  const pace = useRef(15)
  const queue = useRef<Line[]>([])
  const running = useRef(false)
  const round = useRef(0)
  const ctx = useRef<AudioContext | null>(null)
  const playing = useRef(new Set<AudioBufferSourceNode>())
  const aborts = useRef<AbortController | null>(null)
  const ready = useRef<boolean | null>(null)
  const live = useRef(onProblem)
  useEffect(() => {
    live.current = onProblem
  })

  const run = useCallback(async () => {
    if (running.current) return
    running.current = true
    const mine = round.current
    setSpeaking(true)
    const audio = (ctx.current ??= new AudioContext())
    await audio.resume().catch(() => {})
    let cursor = 0 // when the last scheduled piece ends, on the audio clock
    const clock = setInterval(() => {
      if (round.current === mine) setAt(placeOf(heard.current, audio.currentTime, pace.current))
    }, 80)
    const play = (samples: Float32Array, rate: number, line: Heard) => {
      if (!samples.length || round.current !== mine) return
      const buffer = audio.createBuffer(1, samples.length, rate)
      buffer.copyToChannel(samples as Float32Array<ArrayBuffer>, 0)
      const source = audio.createBufferSource()
      source.buffer = buffer
      source.connect(audio.destination)
      const at = Math.max(cursor, audio.currentTime + 0.05)
      source.start(at)
      line.pieces.push({ start: at, frame: FRAME, voiced: voicedFrames(samples, rate) })
      cursor = at + buffer.duration
      playing.current.add(source)
      source.onended = () => playing.current.delete(source)
    }
    try {
      while (queue.current.length && round.current === mine) {
        const line = queue.current.shift()!
        const mark: Heard = { line, pieces: [], arrived: false }
        heard.current.push(mark)
        const abort = new AbortController()
        aborts.current = abort
        const response = await fetch('/api/voice/speak/stream', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(line),
          signal: abort.signal,
        })
        if (!response.ok) throw await problemOf(response)
        setResting(null)
        const rate = Number(response.headers.get('X-Sample-Rate')) || 24000
        const reader = response.body!.getReader()
        const pcm = new Pcm()
        let gathered: Float32Array[] = []
        let length = 0
        // The first piece of a run waits for a short lead, so a slow start does not stutter.
        let want = (cursor > audio.currentTime ? PIECE : LEAD) * rate
        for (;;) {
          const { done, value } = await reader.read()
          if (round.current !== mine) break
          if (value) {
            const samples = pcm.push(value)
            gathered.push(samples)
            length += samples.length
          }
          if (length >= want || (done && length)) {
            play(join(gathered), rate, mark)
            gathered = []
            length = 0
            want = PIECE * rate
          }
          if (done) break
        }
        mark.arrived = true
        const voice = voiceIn(mark)
        if (voice > 0.5) pace.current = (pace.current + line.text.length / voice) / 2
      }
      // Wait for the last words to be heard.
      while (round.current === mine && audio.currentTime < cursor) await new Promise((r) => setTimeout(r, 100))
    } catch (e) {
      if (round.current === mine) {
        if (e instanceof Resting) setResting(e.message)
        else if (!(e instanceof DOMException && e.name === 'AbortError')) live.current(e instanceof Error ? e.message : String(e))
      }
      queue.current = []
      // Her voice gave up: the words go on at reading pace from where they are.
      heard.current = []
      if (round.current === mine) setAt(null)
    } finally {
      clearInterval(clock)
      if (round.current === mine) {
        // All said: the text is shown whole up to the last line; a stop shows it all at once.
        const last = heard.current.at(-1)
        if (last) setAt(last.line.to)
        running.current = false
        aborts.current = null
        setSpeaking(false)
      }
    }
  }, [])

  const available = useCallback(async () => {
    if (ready.current === null) {
      try {
        const status = (await (await fetch('/api/voice/speak/status')).json()) as { ready?: boolean }
        ready.current = !!status.ready
      } catch {
        ready.current = false
      }
      setAnswers(ready.current)
      // Asked again later: her voice may be started after the page.
      setTimeout(() => (ready.current = null), 30_000)
    }
    return ready.current
  }, [])


  /**
   * Queues the parts of an answer: each is split where it changes between Japanese and other
   * scripts, and said in that piece's language (`language` is the one she was spoken to in).
   */
  const say = useCallback(
    async (parts: Spoken[], language: string) => {
      if (!parts.length || !(await available())) return
      queue.current.push(...parts.flatMap(byScript).map((p) => ({ ...p, language: languageOf(p.text, language) })))
      void run()
    },
    [run, available],
  )

  /** A new answer starts: where her voice is starts over from the answer's first words. */
  const begin = useCallback((from = 0) => {
    heard.current = []
    setAt(from)
  }, [])

  const warm = useCallback(async () => {
    if (!(await available())) return
    try {
      const response = await fetch('/api/voice/speak/wake', { method: 'POST' })
      if (response.ok) setResting(null)
      else {
        const problem = await problemOf(response)
        if (problem instanceof Resting) setResting(problem.message)
      }
    } catch {
      // Not reachable: say() will find out, and stay quiet.
    }
  }, [available])

  const stop = useCallback(() => {
    round.current++
    queue.current = []
    aborts.current?.abort()
    aborts.current = null
    for (const source of playing.current) {
      try {
        source.stop()
      } catch {
        // Not started yet or already over.
      }
    }
    playing.current.clear()
    running.current = false
    heard.current = []
    setAt(null)
    setSpeaking(false)
  }, [])

  useEffect(
    () => () => {
      stop()
      void ctx.current?.close().catch(() => {})
      ctx.current = null
    },
    [stop],
  )

  // Asked once on arrival, so the page knows whether answers will be spoken.
  useEffect(() => {
    void available()
  }, [available])

  /** Answers follow her voice when it will speak them: it answers and has room on the card. */
  const voiced = answers === true && !resting

  return { speaking, resting, voiced, at, say, stop, warm, begin }
}
