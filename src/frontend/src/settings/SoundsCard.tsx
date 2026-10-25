import { useState } from 'react'
import { Play, Trash2, Upload } from 'lucide-react'
import { useDeleteSound, useOffices, useSaveSounds, useSounds, useUploadSound } from '../api/hooks'
import type { SoundRule, SoundsView } from '../api/types'
import { useToast } from '../ui/toastContext'
import { playSound } from '../sounds/player'
import { unlockAudio } from '../sounds/synth'
import { BUILTINS, DEFAULT_SOUND, SOUND_EVENTS, soundLabel, type SoundEvent } from '../sounds/rules'

/** Every sound a rule may name, for menus. */
function SoundOptions({ files }: { files: string[] }) {
  return (
    <>
      {Object.entries(BUILTINS).map(([id, label]) => (
        <option key={id} value={`builtin:${id}`}>
          {label}
        </option>
      ))}
      {files.map((f) => (
        <option key={f} value={`file:${f}`}>
          {f} (yours)
        </option>
      ))}
      <option value="silent">Silent</option>
    </>
  )
}

/** Which sound each moment plays, with exceptions for a room or a desk, and your own sound files. */
export function SoundsCard() {
  const sounds = useSounds()
  const offices = useOffices().data ?? []
  const save = useSaveSounds()
  const upload = useUploadSound()
  const remove = useDeleteSound()
  const toast = useToast()
  const [adding, setAdding] = useState<{ scope: string; event: SoundEvent; sound: string }>({ scope: '', event: 'review', sound: 'silent' })
  const config = sounds.data
  if (!config) return null

  const store = (change: Partial<SoundsView>) => save.mutate({ muted: config.muted, volume: config.volume, rules: config.rules, ...change })
  const general = (event: SoundEvent) => config.rules.find((r) => r.event === event && !r.roomId && !r.deskId)?.sound ?? ''
  const setGeneral = (event: SoundEvent, sound: string) =>
    store({
      rules: [
        ...config.rules.filter((r) => !(r.event === event && !r.roomId && !r.deskId)),
        ...(sound ? [{ event, roomId: null, deskId: null, sound }] : []),
      ],
    })
  const exceptions = config.rules.filter((r) => r.roomId || r.deskId)
  const scopeName = (r: SoundRule) => {
    for (const o of offices)
      for (const room of o.rooms) {
        if (room.id === r.roomId) return `${o.name} · room ${room.name}`
        const desk = room.desks.find((d) => d.id === r.deskId)
        if (desk) return `${o.name} · ${room.name} · ${desk.name}`
      }
    return 'A room or desk that no longer exists'
  }
  const addException = () => {
    const [kind, id] = adding.scope.split(':')
    const rule: SoundRule = { event: adding.event, roomId: kind === 'room' ? id : null, deskId: kind === 'desk' ? id : null, sound: adding.sound }
    store({ rules: [...config.rules.filter((r) => !(r.event === rule.event && r.roomId === rule.roomId && r.deskId === rule.deskId)), rule] })
  }

  return (
    <section className="settings-card" aria-labelledby="sounds">
      <h2 id="sounds">Sounds</h2>
      <p className="hint">
        A sound when a moment needs you. They play in this page once you have clicked anywhere in it: browsers keep pages quiet until
        then.
      </p>
      <label className="check-row">
        <input type="checkbox" checked={config.muted} onChange={(e) => store({ muted: e.target.checked })} />
        <span>Mute every sound</span>
      </label>
      <label className="field">
        <span>Volume: {Math.round(config.volume * 100)}%</span>
        <input type="range" min={0} max={1} step={0.05} value={config.volume} onChange={(e) => store({ volume: Number(e.target.value) })} />
      </label>

      <table className="sound-table">
        <tbody>
          {SOUND_EVENTS.map((e) => {
            const chosen = general(e.id)
            return (
              <tr key={e.id}>
                <th scope="row">{e.label}</th>
                <td>
                  <select className="input" value={chosen} onChange={(x) => setGeneral(e.id, x.target.value)}>
                    <option value="">Default ({soundLabel(DEFAULT_SOUND[e.id])})</option>
                    <SoundOptions files={config.files} />
                  </select>
                </td>
                <td>
                  <button type="button" className="btn btn-quiet" aria-label={`Play: ${e.label}`} onClick={() => { unlockAudio(); playSound(chosen || DEFAULT_SOUND[e.id], config.volume) }}>
                    <Play size={16} />
                  </button>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      <h3 className="pixel notes-title">For one room or desk</h3>
      {exceptions.length === 0 ? (
        <p className="hint">None: every room and desk plays the sounds above.</p>
      ) : (
        <ul className="sound-exceptions">
          {exceptions.map((r) => (
            <li key={`${r.event}:${r.roomId}:${r.deskId}`}>
              <span>
                {scopeName(r)} · {SOUND_EVENTS.find((e) => e.id === r.event)?.label}: <strong>{soundLabel(r.sound)}</strong>
              </span>
              <button type="button" className="btn btn-quiet" aria-label="Remove this exception" onClick={() => store({ rules: config.rules.filter((x) => x !== r) })}>
                <Trash2 size={16} />
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="sound-add">
        <select className="input" value={adding.scope} onChange={(e) => setAdding({ ...adding, scope: e.target.value })} aria-label="Room or desk">
          <option value="">Choose a room or desk</option>
          {offices.map((o) => (
            <optgroup key={o.id} label={o.name}>
              {o.rooms.flatMap((room) => [
                <option key={room.id} value={`room:${room.id}`}>
                  Room {room.name}
                </option>,
                ...room.desks.map((d) => (
                  <option key={d.id} value={`desk:${d.id}`}>
                    {'  '}
                    {room.name} · {d.name}
                  </option>
                )),
              ])}
            </optgroup>
          ))}
        </select>
        <select className="input" value={adding.event} onChange={(e) => setAdding({ ...adding, event: e.target.value as SoundEvent })} aria-label="Moment">
          {SOUND_EVENTS.map((e) => (
            <option key={e.id} value={e.id}>
              {e.label}
            </option>
          ))}
        </select>
        <select className="input" value={adding.sound} onChange={(e) => setAdding({ ...adding, sound: e.target.value })} aria-label="Sound">
          <SoundOptions files={config.files} />
        </select>
        <button type="button" className="btn" disabled={!adding.scope || save.isPending} onClick={addException}>
          Add
        </button>
      </div>

      <h3 className="pixel notes-title">Sounds of your own</h3>
      <p className="hint">They stay on this machine, in a folder git ignores: never in a commit, never on GitHub. Fine for sounds you may use only yourself.</p>
      {config.files.length > 0 && (
        <ul className="sound-exceptions">
          {config.files.map((f) => (
            <li key={f}>
              <span className="mono">{f}</span>
              <span className="row">
                <button type="button" className="btn btn-quiet" aria-label={`Play ${f}`} onClick={() => { unlockAudio(); playSound(`file:${f}`, config.volume) }}>
                  <Play size={16} />
                </button>
                <button
                  type="button"
                  className="btn btn-quiet"
                  aria-label={`Delete ${f}`}
                  onClick={() => remove.mutate(f, { onSuccess: () => toast(`${f} deleted: its moments play their default again`) })}
                >
                  <Trash2 size={16} />
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}
      <label className="btn sound-upload">
        <Upload size={16} aria-hidden /> Add a sound file
        <input
          type="file"
          accept="audio/*,.mp3,.ogg,.wav,.m4a,.webm,.opus,.flac"
          className="sr-only"
          onChange={(e) => {
            const file = e.target.files?.[0]
            if (file) upload.mutate(file, { onSuccess: (r) => toast(`${r.name} added: choose it for a moment above`) })
            e.target.value = ''
          }}
        />
      </label>
      {(save.error ?? upload.error ?? remove.error) && <p className="error">{(save.error ?? upload.error ?? remove.error)?.message}</p>}
    </section>
  )
}
