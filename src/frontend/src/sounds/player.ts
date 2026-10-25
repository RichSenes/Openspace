import type { BuiltinSound } from './rules'
import { playBuiltin } from './synth'

/** Plays a sound as a rule names it: built in, a file of yours, or nothing. */
export function playSound(sound: string, volume: number) {
  if (sound.startsWith('builtin:')) {
    playBuiltin(sound.slice(8) as BuiltinSound, volume)
  } else if (sound.startsWith('file:')) {
    const audio = new Audio(`/api/sounds/files/${encodeURIComponent(sound.slice(5))}`)
    audio.volume = Math.max(0, Math.min(1, volume))
    void audio.play().catch(() => undefined)
  }
}
