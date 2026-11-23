import { useCallback, useEffect, useRef, useState } from 'react'
import { MAX_MS, MIN_MS, loudness, pickMime, type Ears, type Heard } from './voice'

/** Sends a recording to Openspace, which asks the listening server on this machine. */
async function hear(blob: Blob): Promise<Heard> {
  const response = await fetch('/api/voice/hear', { method: 'POST', body: blob, headers: { 'Content-Type': blob.type || 'application/octet-stream' } })
  if (!response.ok) {
    let detail = `The voice could not be heard (${response.status}).`
    try {
      const problem = (await response.json()) as { detail?: string; title?: string }
      detail = problem.detail ?? problem.title ?? detail
    } catch {
      // Not JSON: keep the status.
    }
    throw new Error(detail)
  }
  return (await response.json()) as Heard
}

/**
 * The microphone, held to talk. `level` is the live loudness (0..1) for Shiori to react to;
 * `onHeard` gets the words once the listening server has them.
 */
export function useEars(onHeard: (heard: Heard) => void, onProblem: (message: string) => void) {
  const [ears, setEars] = useState<Ears>(() => (typeof MediaRecorder === 'undefined' || !navigator.mediaDevices ? 'unavailable' : 'idle'))
  const [ms, setMs] = useState(0)
  const [level, setLevel] = useState(0)
  const live = useRef({ onHeard, onProblem })
  useEffect(() => {
    live.current = { onHeard, onProblem }
  })
  const rec = useRef<{ recorder: MediaRecorder; stream: MediaStream; started: number; chunks: Blob[]; raf: number; timer: number; ctx: AudioContext | null; wanted: boolean } | null>(null)

  const stopTracks = () => {
    const r = rec.current
    if (!r) return
    cancelAnimationFrame(r.raf)
    clearInterval(r.timer)
    r.stream.getTracks().forEach((t) => t.stop())
    void r.ctx?.close()
    setLevel(0)
  }

  const start = useCallback(async () => {
    if (rec.current || ears === 'unavailable' || ears === 'hearing') return
    setEars('asking')
    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } })
    } catch {
      setEars('denied')
      return
    }
    const mime = pickMime((m) => MediaRecorder.isTypeSupported(m))
    const recorder = new MediaRecorder(stream, mime ? { mimeType: mime } : undefined)
    const chunks: Blob[] = []
    recorder.ondataavailable = (e) => e.data.size && chunks.push(e.data)
    let ctx: AudioContext | null = null
    let analyser: AnalyserNode | null = null
    try {
      ctx = new AudioContext()
      analyser = ctx.createAnalyser()
      analyser.fftSize = 512
      ctx.createMediaStreamSource(stream).connect(analyser)
    } catch {
      ctx = null
    }
    const frame = new Uint8Array(512)
    const started = performance.now()
    const r = { recorder, stream, started, chunks, raf: 0, timer: 0, ctx, wanted: true }
    const tick = () => {
      if (analyser) {
        analyser.getByteTimeDomainData(frame)
        setLevel(loudness(frame))
      }
      r.raf = requestAnimationFrame(tick)
    }
    r.raf = requestAnimationFrame(tick)
    r.timer = window.setInterval(() => {
      const t = performance.now() - started
      setMs(t)
      if (t > MAX_MS) recorder.stop()
    }, 200)
    recorder.onstop = async () => {
      stopTracks()
      const length = performance.now() - r.started
      rec.current = null
      setMs(0)
      if (length < MIN_MS || !r.wanted) {
        setEars('idle')
        return
      }
      setEars('hearing')
      try {
        live.current.onHeard(await hear(new Blob(chunks, { type: recorder.mimeType })))
      } catch (e) {
        live.current.onProblem(e instanceof Error ? e.message : String(e))
      } finally {
        setEars('idle')
      }
    }
    rec.current = r
    recorder.start(250)
    setEars('recording')
  }, [ears])

  /** Let go: send what was said. */
  const stop = useCallback(() => {
    const r = rec.current
    if (r && r.recorder.state !== 'inactive') r.recorder.stop()
  }, [])

  /** Changed mind: drop it. */
  const cancel = useCallback(() => {
    const r = rec.current
    if (!r) return
    r.wanted = false
    if (r.recorder.state !== 'inactive') r.recorder.stop()
  }, [])

  useEffect(() => () => {
    const r = rec.current
    if (r) {
      r.wanted = false
      if (r.recorder.state !== 'inactive') r.recorder.stop()
    }
  }, [])

  return { ears, ms, level, start, stop, cancel }
}
