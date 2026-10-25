import { useEffect, useRef } from 'react'
import { useEngines, useOffices, useSounds } from '../api/hooks'
import { playSound } from './player'
import { emitCue, onCue, resolveSound } from './rules'

/** Plays the sound of each moment the live updates announce, as the sound settings say. */
export function useSoundCues() {
  const sounds = useSounds().data
  const offices = useOffices().data
  const latest = useRef({ sounds, offices })
  const played = useRef(new Set<string>())
  const pauses = useEngines().data?.pauses
  const knownPauses = useRef<Set<string> | undefined>(undefined)

  // A new pause for an account's limits is a moment; the ones already there when the page opened are not.
  useEffect(() => {
    if (!pauses) return
    const now = new Set(pauses.map((p) => `${p.scope}:${p.until}`))
    if (knownPauses.current) for (const key of now) if (!knownPauses.current.has(key)) emitCue({ event: 'paused', key })
    knownPauses.current = now
  }, [pauses])

  useEffect(() => {
    latest.current = { sounds, offices }
  }, [sounds, offices])

  useEffect(
    () =>
      onCue((cue) => {
        const { sounds: config, offices: all } = latest.current
        if (!config || config.muted || played.current.has(cue.key)) return
        if (played.current.size > 500) played.current.clear()
        played.current.add(cue.key)
        const roomId = all?.flatMap((o) => o.rooms).find((r) => r.desks.some((d) => d.id === cue.deskId))?.id
        const sound = resolveSound(config, cue.event, cue.deskId, roomId)
        if (sound !== 'silent') playSound(sound, config.volume)
      }),
    [],
  )
}
