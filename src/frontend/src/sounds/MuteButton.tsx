import { useSyncExternalStore } from 'react'
import { Volume2, VolumeX } from 'lucide-react'
import { useSaveSounds, useSounds } from '../api/hooks'
import { audioReady, onAudioReady, unlockAudio } from './synth'

/** Silences every sound at once or brings them back; until the page was clicked once, says sounds wait for that. */
export function MuteButton() {
  const config = useSounds().data
  const save = useSaveSounds()
  const ready = useSyncExternalStore(onAudioReady, audioReady)
  if (!config) return null

  if (!ready && !config.muted) {
    return (
      <button type="button" className="btn btn-quiet sound-blocked" title="Browsers keep a page quiet until you click it" onClick={unlockAudio}>
        <VolumeX size={16} aria-hidden /> Click to turn sounds on
      </button>
    )
  }

  const label = config.muted ? 'Sounds are off: turn them on' : 'Sounds are on: mute them'
  return (
    <button
      type="button"
      className="btn btn-quiet"
      aria-label={label}
      title={label}
      aria-pressed={config.muted}
      disabled={save.isPending}
      onClick={() => save.mutate({ muted: !config.muted, volume: config.volume, rules: config.rules })}
    >
      {config.muted ? <VolumeX size={16} /> : <Volume2 size={16} />}
    </button>
  )
}
