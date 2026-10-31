import { useEffect, useMemo, useState } from 'react'
import { Check, Shuffle } from 'lucide-react'
import { useDressDesk } from '../api/hooks'
import type { DeskView } from '../api/types'
import { poseOf } from '../art/agentStates'
import { toCanvas } from '../art/canvas'
import { CLOTH_COLORS, HAIR_COLORS, SKIN_TONES } from '../art/palette'
import { STATION, stationPixels, type Appearance } from '../art/station'
import { themeOf, type Theme } from '../art/themes'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import { furnitureLabel } from './arrange'
import { deskLookOf, LOOK_PARTS, lookOf, shuffledLook } from './agents'
import './look.css'

/** Frames per second of the pixel art, as in the building. */
const FPS = 5

/** The agent at its desk, as a picture: typing away, or one still frame when motion is reduced. */
function usePortrait(look: Appearance, theme: Theme, animate = true): string {
  const [tick, setTick] = useState(0)
  useEffect(() => {
    if (!animate || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
    const timer = setInterval(() => setTick((t) => t + 1), 1000 / FPS)
    return () => clearInterval(timer)
  }, [animate])
  return useMemo(
    () => toCanvas(STATION.width, STATION.height, stationPixels(look, poseOf('working', tick), theme)).toDataURL(),
    [look, theme, tick],
  )
}

/** A small still picture of a desk's agent, for lists. */
export function Portrait({ desk, theme, label, visitor }: { desk: DeskView; theme: string; label?: string; visitor?: boolean }) {
  const src = usePortrait(visitor ? { ...lookOf(desk), visitor } : lookOf(desk), themeOf(theme), false)
  return <img className="portrait" src={src} alt={label ?? ''} width={STATION.width} height={STATION.height} />
}

interface Props {
  officeId: string
  theme: string
  desk: DeskView | undefined
  onClose: () => void
}

/** Choosing how the agent at a desk looks, part by part, with the agent shown as it will be. */
export function LookDialog({ officeId, theme, desk, onClose }: Props) {
  return (
    <Dialog title={desk ? `How ${desk.name} looks` : 'Look'} open={Boolean(desk)} onClose={onClose}>
      {desk && <LookForm key={desk.id} officeId={officeId} theme={theme} desk={desk} onClose={onClose} />}
    </Dialog>
  )
}

function LookForm({ officeId, theme, desk, onClose }: Required<Pick<Props, 'officeId' | 'theme' | 'onClose'>> & { desk: DeskView }) {
  const [look, setLook] = useState<Appearance>(() => lookOf(desk))
  const portrait = usePortrait(look, themeOf(theme))
  const dress = useDressDesk(officeId)
  const toast = useToast()
  const set = (change: Partial<Appearance>) => setLook((l) => ({ ...l, ...change }))
  const costumed = Boolean(look.costume)

  const save = (chosen: Appearance | null) =>
    dress.mutate(
      { deskId: desk.id, look: chosen && deskLookOf(chosen) },
      {
        onSuccess: () => {
          toast(chosen ? `${desk.name} dressed` : `${desk.name} looks as picked again`)
          onClose()
        },
      },
    )

  return (
    <form
      className="look-form"
      onSubmit={(e) => {
        e.preventDefault()
        save(look)
      }}
    >
      <div className="look-preview">
        <img src={portrait} alt={`${desk.name} at the desk`} width={STATION.width * 4} height={STATION.height * 4} />
        <button type="button" className="btn" onClick={() => setLook((l) => ({ ...shuffledLook(), costume: l.costume }))}>
          <Shuffle size={16} /> Shuffle
        </button>
      </div>

      <div className="look-parts">
        <Choice label="Costume" hint="Replaces face, hair and clothes" value={look.costume} options={LOOK_PARTS.costume} optional onChange={(costume) => set({ costume })} />
        <Choice label="Skin" value={look.skin} options={LOOK_PARTS.skin} swatch={(s) => SKIN_TONES[s][0]} disabled={costumed} onChange={(skin) => skin && set({ skin })} />
        <Choice label="Hair" value={look.hair} options={LOOK_PARTS.hair} swatch={(h) => HAIR_COLORS[h][0]} disabled={costumed} onChange={(hair) => hair && set({ hair })} />
        <Choice label="Hair style" value={look.hairStyle} options={LOOK_PARTS.hairStyle} disabled={costumed} onChange={(hairStyle) => hairStyle && set({ hairStyle })} />
        <Choice label="Clothes" value={look.cloth} options={LOOK_PARTS.cloth} swatch={(c) => CLOTH_COLORS[c][0]} disabled={costumed} onChange={(cloth) => cloth && set({ cloth })} />
        <Choice label="Outfit" value={look.outfit} options={LOOK_PARTS.outfit} optional disabled={costumed} onChange={(outfit) => set({ outfit })} />
        <Choice label="Accessory" value={look.accessory} options={LOOK_PARTS.accessory} optional disabled={costumed} onChange={(accessory) => set({ accessory })} />
      </div>

      {dress.error && <p className="error">{dress.error.message}</p>}
      <div className="row look-actions">
        <button type="button" className="btn btn-quiet" disabled={!desk.look || dress.isPending} onClick={() => save(null)}>
          Back to the picked look
        </button>
        <button type="button" className="btn" onClick={onClose}>
          Cancel
        </button>
        <button type="submit" className="btn btn-primary" disabled={dress.isPending}>
          Save look
        </button>
      </div>
    </form>
  )
}

interface ChoiceProps<T extends string> {
  label: string
  hint?: string
  value: T | undefined
  options: readonly T[]
  /** Offers "None" first. */
  optional?: boolean
  /** A colour shown beside the name: never on its own, the name always says it. */
  swatch?: (option: T) => string
  disabled?: boolean
  onChange: (value: T | undefined) => void
}

/** One part of the look: a row of named chips, the chosen one ticked. */
function Choice<T extends string>({ label, hint, value, options, optional, swatch, disabled, onChange }: ChoiceProps<T>) {
  const chips: (T | undefined)[] = optional ? [undefined, ...options] : [...options]
  return (
    <fieldset className="look-part" disabled={disabled}>
      <legend>
        {label}
        {hint && <span className="hint"> · {hint}</span>}
      </legend>
      <div className="chips">
        {chips.map((option) => {
          const chosen = option === value
          return (
            <button key={option ?? 'none'} type="button" className="chip" aria-pressed={chosen} onClick={() => onChange(option)}>
              {chosen && <Check size={14} aria-hidden />}
              {option && swatch && <span className="swatch" style={{ background: swatch(option) }} aria-hidden />}
              {option ? furnitureLabel(option) : 'None'}
            </button>
          )
        })}
      </div>
    </fieldset>
  )
}
