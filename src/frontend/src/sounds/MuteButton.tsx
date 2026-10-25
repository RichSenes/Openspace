import { Volume2, VolumeX } from 'lucide-react'
import { useSaveSounds, useSounds } from '../api/hooks'

/** Silences every sound at once, or brings them back: the quick way, without opening Settings. */
export function MuteButton() {
  const config = useSounds().data
  const save = useSaveSounds()
  if (!config) return null
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
