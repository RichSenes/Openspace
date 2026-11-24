import { useCallback, useEffect, useRef, useState } from 'react'
import { join, Pcm } from './pcm'

interface Line {
  text: string
  language: string
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
    const play = (samples: Float32Array, rate: number) => {
      if (!samples.length || round.current !== mine) return
      const buffer = audio.createBuffer(1, samples.length, rate)
      buffer.copyToChannel(samples as Float32Array<ArrayBuffer>, 0)
      const source = audio.createBufferSource()
      source.buffer = buffer
      source.connect(audio.destination)
      const at = Math.max(cursor, audio.currentTime + 0.05)
      source.start(at)
      cursor = at + buffer.duration
      playing.current.add(source)
      source.onended = () => playing.current.delete(source)
    }
    try {
      while (queue.current.length && round.current === mine) {
        const line = queue.current.shift()!
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
            play(join(gathered), rate)
            gathered = []
            length = 0
            want = PIECE * rate
          }
          if (done) break
        }
      }
      // Wait for the last words to be heard.
      while (round.current === mine && audio.currentTime < cursor) await new Promise((r) => setTimeout(r, 100))
    } catch (e) {
      if (round.current === mine) {
        if (e instanceof Resting) setResting(e.message)
        else if (!(e instanceof DOMException && e.name === 'AbortError')) live.current(e instanceof Error ? e.message : String(e))
      }
      queue.current = []
    } finally {
      if (round.current === mine) {
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
      // Asked again later: her voice may be started after the page.
      setTimeout(() => (ready.current = null), 30_000)
    }
    return ready.current
  }, [])

  const say = useCallback(
    async (sentences: string[], language: string) => {
      if (!sentences.length || !(await available())) return
      queue.current.push(...sentences.map((text) => ({ text, language })))
      void run()
    },
    [run, available],
  )

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

  return { speaking, resting, say, stop, warm }
}
