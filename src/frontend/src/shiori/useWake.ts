import { useCallback, useEffect, useRef, useState } from 'react'
import type { Heard } from './voice'
import { QUIET_MS, TURN_MS, Utterances, isGoodbye, resample, toWav, type Wake } from './wake'

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

/** Two soft notes: rising when she starts listening, falling when the conversation closes. */
function chime(ctx: AudioContext, up: boolean) {
  const now = ctx.currentTime
  const notes = up ? [660, 990] : [880, 587]
  notes.forEach((hz, i) => {
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.type = 'sine'
    osc.frequency.value = hz
    const at = now + i * 0.11
    gain.gain.setValueAtTime(0, at)
    gain.gain.linearRampToValueAtTime(0.12, at + 0.015)
    gain.gain.exponentialRampToValueAtTime(0.001, at + 0.22)
    osc.connect(gain).connect(ctx.destination)
    osc.start(at)
    osc.stop(at + 0.25)
  })
}

/**
 * Always listening for her name. While `on`, the microphone stays open and each sentence is
 * checked for "Shiori…"; her name opens a conversation in which every sentence goes to
 * `onRequest`, until a goodbye, a minute of quiet, or `end()`. `paused` (holding the mic to
 * talk) sets it aside meanwhile.
 */
export function useWake(on: boolean, paused: boolean, onRequest: (heard: Heard) => void, onProblem: (message: string) => void) {
  const canListen = typeof AudioWorkletNode !== 'undefined' && !!navigator.mediaDevices
  const [wake, setWake] = useState<Wake>('off')
  const live = useRef({ onRequest, onProblem, paused })
  const closer = useRef<() => void>(() => {})
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
    let talking = false
    let quietTimer = 0
    let turnTimer = 0
    // The sentences of the turn being said, sent together once the person stops.
    let turn: string[] = []
    let language = ''
    let busy = false
    const queue: Float32Array[] = []
    const cut = new Utterances()

    const settle = () => setWake(talking ? 'talking' : 'waiting')
    const keepOpen = () => {
      clearTimeout(quietTimer)
      quietTimer = window.setTimeout(() => close(), QUIET_MS)
    }
    const open = () => {
      if (!talking && ctx) chime(ctx, true)
      talking = true
      keepOpen()
      settle()
    }
    const close = () => {
      if (talking && ctx) chime(ctx, false)
      talking = false
      turn = []
      clearTimeout(turnTimer)
      clearTimeout(quietTimer)
      if (!stopped) settle()
    }
    closer.current = close

    const endOfTurn = () => {
      clearTimeout(turnTimer)
      turnTimer = window.setTimeout(() => {
        // Still talking, or a sentence still being heard: the turn is not over.
        if (cut.speaking || busy || queue.length) return endOfTurn()
        // A goodbye said as its own last sentence closes too, after what came before it.
        const last = turn.length > 1 && isGoodbye(turn[turn.length - 1])
        const said = (last ? turn.slice(0, -1) : turn).join(' ').trim()
        turn = []
        if (!said || stopped) return
        if (isGoodbye(said)) return close()
        live.current.onRequest({ text: said, language, seconds: 0 })
        if (last) close()
      }, TURN_MS)
    }

    const hear = async (sentence: Float32Array) => {
      const inConversation = talking
      if (inConversation) {
        keepOpen()
        setWake('hearing')
      }
      const heard = await ask(toWav(sentence), !inConversation)
      if (stopped) return
      if (!inConversation && !heard.called) return
      if (!inConversation) open()
      if (heard.text.trim()) {
        turn.push(heard.text.trim())
        language = heard.language
        endOfTurn()
      }
    }

    // One sentence at a time, in the order they were said.
    const next = async () => {
      if (busy) return
      busy = true
      while (queue.length && !stopped) {
        try {
          await hear(queue.shift()!)
        } catch (e) {
          if (!stopped) live.current.onProblem(e instanceof Error ? e.message : String(e))
        }
        if (!stopped) settle()
      }
      busy = false
    }

    const begin = async () => {
      setWake('starting')
      try {
        // No automatic gain: raising the room's hum between words keeps sentences from ending.
        stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: false } })
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
        const said = cut.push(resample(e.data, rate))
        if (!said.length) return
        // Before her name only the latest sentence matters; in a conversation every one does.
        if (talking) queue.push(...said)
        else queue.splice(0, queue.length, said[said.length - 1])
        void next()
      }
      setWake('waiting')
    }
    void begin().catch((e) => {
      if (!stopped) live.current.onProblem(e instanceof Error ? e.message : String(e))
    })

    return () => {
      stopped = true
      closer.current = () => {}
      clearTimeout(quietTimer)
      clearTimeout(turnTimer)
      stream?.getTracks().forEach((t) => t.stop())
      nodes.forEach((n) => n.disconnect())
      nodes = []
      void ctx?.close()
      setWake('off')
    }
  }, [on, canListen])

  const end = useCallback(() => closer.current(), [])
  return { wake: canListen ? wake : on ? ('unavailable' as const) : ('off' as const), end }
}
