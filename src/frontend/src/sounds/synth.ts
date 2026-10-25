import type { BuiltinSound } from './rules'

/** A note: its pitch in hertz, when it starts and how long it lasts, in seconds. */
type Note = [frequency: number, start: number, length: number]

/** Square and triangle waves, a few notes each: sounds in the spirit of the pixel art. */
const SOUNDS: Record<BuiltinSound, { wave: OscillatorType; notes: Note[] }> = {
  chime: { wave: 'square', notes: [[659, 0, 0.09], [880, 0.1, 0.16]] },
  knock: { wave: 'triangle', notes: [[196, 0, 0.07], [196, 0.14, 0.07], [262, 0.3, 0.12]] },
  levelUp: { wave: 'square', notes: [[523, 0, 0.07], [659, 0.07, 0.07], [784, 0.14, 0.07], [1047, 0.21, 0.2]] },
  fail: { wave: 'square', notes: [[392, 0, 0.14], [330, 0.15, 0.14], [262, 0.3, 0.3]] },
  coin: { wave: 'square', notes: [[988, 0, 0.07], [1319, 0.07, 0.25]] },
  blip: { wave: 'square', notes: [[880, 0, 0.06]] },
  powerDown: { wave: 'square', notes: [[660, 0, 0.08], [494, 0.08, 0.08], [370, 0.16, 0.08], [247, 0.24, 0.25]] },
  fanfare: { wave: 'square', notes: [[784, 0, 0.08], [784, 0.1, 0.08], [784, 0.2, 0.08], [1047, 0.32, 0.35]] },
  alarm: { wave: 'square', notes: [[880, 0, 0.12], [660, 0.14, 0.12], [880, 0.28, 0.12], [660, 0.42, 0.12]] },
}

let context: AudioContext | undefined
const listeners = new Set<() => void>()

/**
 * Browsers let a page make sound only once the person clicked or typed in it, and the
 * audio must be switched on inside that very click: this does it, on the first one.
 */
export function unlockAudio() {
  context ??= new AudioContext()
  if (context.state !== 'running') {
    void context.resume().then(() => listeners.forEach((l) => l()))
  }
}

/** Whether the page may make sound now. */
export const audioReady = () => context?.state === 'running'

/** Tells when the page becomes able to make sound. */
export function onAudioReady(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Plays a built-in sound; silent until the page was clicked once. */
export function playBuiltin(name: BuiltinSound, volume: number) {
  const sound = SOUNDS[name]
  if (!sound || !context) return
  if (context.state !== 'running') {
    // Switched on a moment ago, in this very click: play once it is ready, never much later.
    const asked = performance.now()
    void context.resume().then(() => {
      if (performance.now() - asked < 1000) playBuiltin(name, volume)
    })
    return
  }
  const now = context.currentTime
  for (const [frequency, start, length] of sound.notes) {
    const oscillator = context.createOscillator()
    const gain = context.createGain()
    oscillator.type = sound.wave
    oscillator.frequency.value = frequency
    // Square waves are loud: a quarter of the volume, with a short fade so notes do not click.
    const peak = Math.max(0, Math.min(1, volume)) * 0.25
    gain.gain.setValueAtTime(0, now + start)
    gain.gain.linearRampToValueAtTime(peak, now + start + 0.01)
    gain.gain.exponentialRampToValueAtTime(0.0001, now + start + length)
    oscillator.connect(gain).connect(context.destination)
    oscillator.start(now + start)
    oscillator.stop(now + start + length + 0.02)
  }
}
