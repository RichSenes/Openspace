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
}

let context: AudioContext | undefined

/** Plays a built-in sound. Browsers let a page make sound only after the person clicked it once. */
export function playBuiltin(name: BuiltinSound, volume: number) {
  const sound = SOUNDS[name]
  if (!sound) return
  context ??= new AudioContext()
  void context.resume()
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
