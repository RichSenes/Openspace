import { useEffect, useRef, useState } from 'react'
import type { Heard } from './voice'
import { CALLED_MS, Utterances, resample, toWav, type Wake } from './wake'

// Hands the raw microphone to the page in pieces; nothing is played back.
const TAP = `class Tap extends AudioWorkletProcessor {
  constructor() { super(); this.buf = new Float32Array(2048); this.n = 0 }
  process(inputs) {
    // No sound at all (a source that went quiet) counts as silence, so a sentence still ends.
    const ch = (inputs[0] && inputs[0][0]) || null
    for (let i = 0; i < 128; i++) {
      this.buf[this.n++] = ch ? ch[i] : 0
      if (this.n === this.buf.length) { this.port.postMessage(this.buf.slice(0)); this.n = 0 }
    }
    return true
  }
}
registerProcessor('shiori-tap', Tap)`

async function ask(wav: ArrayBuffer, wake: boolean): Promise<Heard & { called: boolean }> {
  const response = await fetch(`/api/voice/hear${wake ? '?wake=true' : ''}`, { method: 'POST', body: wav, headers: { 'Content-Type': 'audio/wav' } })
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
  return (await response.json()) as Heard & { called: boolean }
}

/**
 * Always listening for her name. While `on`, the microphone stays open and each sentence is
 * checked for "Shiori…"; `onRequest` gets what was asked after it (or the next sentence, when
 * only her name was said). `paused` (holding the mic to talk) sets it aside meanwhile.
 */
export function useWake(on: boolean, paused: boolean, onRequest: (heard: Heard) => void, onProblem: (message: string) => void) {
  const canListen = typeof AudioWorkletNode !== 'undefined' && !!navigator.mediaDevices
  const [wake, setWake] = useState<Wake>('off')
  const live = useRef({ onRequest, onProblem, paused })
  useEffect(() => {
    live.current = { onRequest, onProblem, paused }
  })

  useEffect(() => {
    if (!on || !canListen) return
    let stopped = false
    let stream: MediaStream | null = null
    let ctx: AudioContext | null = null
    // Held here on purpose: Firefox lets go of audio nodes nobody holds, and the sound stops.
    let nodes: AudioNode[] = []
    let calledUntil = 0
    let calledTimer = 0
    let busy = false
    let waiting: Float32Array | null = null
    const cut = new Utterances()
    const settle = () => setWake(performance.now() < calledUntil ? 'called' : 'waiting')

    const hear = async (sentence: Float32Array) => {
      if (busy) {
        waiting = sentence // only the latest one waits; the room may talk faster than she hears
        return
      }
      busy = true
      const afterName = performance.now() < calledUntil
      if (afterName) setWake('hearing')
      try {
        const heard = await ask(toWav(sentence), !afterName)
        if (stopped) return
        if (afterName) {
          calledUntil = 0
          if (heard.text.trim()) live.current.onRequest(heard)
        } else if (heard.called) {
          if (heard.text.trim()) live.current.onRequest(heard)
          else {
            // Only her name: she turns to listen for a moment.
            calledUntil = performance.now() + CALLED_MS
            clearTimeout(calledTimer)
            calledTimer = window.setTimeout(settle, CALLED_MS + 50)
          }
        }
      } catch (e) {
        if (!stopped) live.current.onProblem(e instanceof Error ? e.message : String(e))
      } finally {
        busy = false
        if (!stopped) settle()
        const next = waiting
        waiting = null
        if (next && !stopped) void hear(next)
      }
    }

    const begin = async () => {
      setWake('starting')
      try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } })
      } catch {
        if (!stopped) setWake('denied')
        return
      }
      if (stopped) {
        stream.getTracks().forEach((t) => t.stop())
        return
      }
      ctx = new AudioContext()
      const url = URL.createObjectURL(new Blob([TAP], { type: 'text/javascript' }))
      try {
        await ctx.audioWorklet.addModule(url)
      } finally {
        URL.revokeObjectURL(url)
      }
      if (stopped) return
      const tap = new AudioWorkletNode(ctx, 'shiori-tap')
      const mute = ctx.createGain()
      mute.gain.value = 0
      const source = ctx.createMediaStreamSource(stream)
      source.connect(tap).connect(mute).connect(ctx.destination)
      nodes = [source, tap, mute]
      const rate = ctx.sampleRate
      // Opened after the click's moment has passed, the browser may start it paused.
      await ctx.resume().catch(() => {})
      tap.port.onmessage = (e: MessageEvent<Float32Array>) => {
        if (live.current.paused) {
          cut.reset()
          return
        }
        for (const sentence of cut.push(resample(e.data, rate))) void hear(sentence)
      }
      setWake('waiting')
    }
    void begin().catch((e) => {
      if (!stopped) live.current.onProblem(e instanceof Error ? e.message : String(e))
    })

    return () => {
      stopped = true
      clearTimeout(calledTimer)
      stream?.getTracks().forEach((t) => t.stop())
      nodes.forEach((n) => n.disconnect())
      nodes = []
      void ctx?.close()
      setWake('off')
    }
  }, [on, canListen])

  return canListen ? wake : on ? 'unavailable' : 'off'
}
