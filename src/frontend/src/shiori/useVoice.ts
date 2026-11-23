import { useCallback, useEffect, useRef, useState } from 'react'

interface Line {
  text: string
  language: string
}

async function voiceOf(line: Line): Promise<Blob> {
  const response = await fetch('/api/voice/speak', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(line) })
  if (!response.ok) {
    let detail = `Her voice could not speak (${response.status}).`
    try {
      const problem = (await response.json()) as { detail?: string; title?: string }
      detail = problem.detail ?? problem.title ?? detail
    } catch {
      // Not JSON: keep the status.
    }
    throw new Error(detail)
  }
  return response.blob()
}

/**
 * Shiori's voice: `say` queues sentences, which are spoken in order; the next one is asked for
 * while the current one plays, so there are no gaps. `stop` silences her and drops the rest.
 * When her voice is not running she simply stays silent.
 */
export function useVoice(onProblem: (message: string) => void) {
  const [speaking, setSpeaking] = useState(false)
  const queue = useRef<Line[]>([])
  const running = useRef(false)
  const round = useRef(0)
  const player = useRef<HTMLAudioElement | null>(null)
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
    let next: Promise<Blob> | null = null
    try {
      while (queue.current.length && round.current === mine) {
        const blob = await (next ?? voiceOf(queue.current[0]))
        queue.current.shift()
        next = queue.current.length ? voiceOf(queue.current[0]) : null
        next?.catch(() => {}) // its failure is met when it is played
        if (round.current !== mine) break
        const url = URL.createObjectURL(blob)
        try {
          await new Promise<void>((resolve) => {
            const audio = new Audio(url)
            player.current = audio
            audio.onended = () => resolve()
            audio.onerror = () => resolve()
            audio.onpause = () => resolve()
            void audio.play().catch(() => resolve())
          })
        } finally {
          URL.revokeObjectURL(url)
          player.current = null
        }
      }
    } catch (e) {
      if (round.current === mine) live.current(e instanceof Error ? e.message : String(e))
      queue.current = []
    } finally {
      running.current = false
      if (round.current === mine) setSpeaking(false)
    }
  }, [])

  const say = useCallback(
    async (sentences: string[], language: string) => {
      if (!sentences.length) return
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
      if (!ready.current) return
      queue.current.push(...sentences.map((text) => ({ text, language })))
      void run()
    },
    [run],
  )

  const stop = useCallback(() => {
    round.current++
    queue.current = []
    player.current?.pause()
    running.current = false
    setSpeaking(false)
  }, [])

  useEffect(() => () => stop(), [stop])

  return { speaking, say, stop }
}
