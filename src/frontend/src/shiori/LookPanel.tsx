import { X } from 'lucide-react'
import type { ReactNode, Ref } from 'react'
import type { Sky } from '../api/types'
import { RIGS } from '../lab/presence/body/rig'
import { HOURS, SKIES, type Hour } from '../lab/presence/world/types'
import { WORLD_ORDER, WORLDS } from '../lab/presence/world/worlds'
import { PIXELS, SKINS, type Look } from './look'

interface Props {
  look: Look
  onPick: (change: Partial<Look>) => void
  onClose: () => void
  clockHour: Hour
  realSky: Sky | undefined
  systemCalm: boolean
  ref?: Ref<HTMLElement>
}

function Chip({ on, onClick, children }: { on: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button type="button" className={`pr-chip${on ? ' is-on' : ''}`} aria-pressed={on} onClick={onClick}>
      {children}
    </button>
  )
}

/** How Shiori and her world look, for this viewer only. */
export function LookPanel({ look, onPick, onClose, clockHour, realSky, systemCalm, ref }: Props) {
  const world = WORLDS[look.world]
  return (
    <aside ref={ref} className="pr-lab sp-look" aria-label="Look">
      <div className="pr-drawer-head">
        <div className="pr-lab-head">
          <span>Look</span>
          <span className="pr-muted">Kept in this browser only</span>
        </div>
        <button type="button" className="pr-icon" onClick={onClose} aria-label="Close look">
          <X size={18} />
        </button>
      </div>
      <fieldset>
        <legend>Body</legend>
        <div className="pr-chips">
          {SKINS.map((s) => (
            <Chip key={s} on={look.skin === s} onClick={() => onPick({ skin: s })}>
              {RIGS[s].label}
            </Chip>
          ))}
        </div>
        <p className="pr-muted">{RIGS[look.skin].line}</p>
      </fieldset>
      <fieldset>
        <legend>Drawing</legend>
        <div className="pr-chips">
          {PIXELS.map((p) => (
            <Chip key={p} on={look.pixel === p} onClick={() => onPick({ pixel: p })}>
              {p === 0 ? 'Smooth' : `Pixel ×${p}`}
            </Chip>
          ))}
        </div>
        <p className="pr-muted">Pixel: Shiori on the world's own grid and palette.</p>
      </fieldset>
      <fieldset>
        <legend>World</legend>
        <div className="pr-chips">
          {WORLD_ORDER.map((id) => (
            <Chip key={id} on={look.world === id} onClick={() => onPick({ world: id })}>
              {WORLDS[id].label}
            </Chip>
          ))}
        </div>
        <p className="pr-muted">{world.line}</p>
      </fieldset>
      <fieldset>
        <legend>Hour</legend>
        <div className="pr-chips">
          {(['auto', ...HOURS] as const).map((h) => (
            <Chip key={h} on={look.hour === h} onClick={() => onPick({ hour: h })}>
              {h === 'auto' ? `Now · ${world.hours[clockHour]}` : world.hours[h]}
            </Chip>
          ))}
        </div>
      </fieldset>
      <fieldset>
        <legend>Weather</legend>
        <div className="pr-chips">
          <Chip on={look.sky === 'auto'} onClick={() => onPick({ sky: 'auto' })}>
            {realSky ? `Now · ${world.skies[realSky]}` : 'Now'}
          </Chip>
          {SKIES.map((k) => (
            <Chip key={k} on={look.sky === k} onClick={() => onPick({ sky: k })}>
              {world.skies[k]}
            </Chip>
          ))}
        </div>
        <p className="pr-muted">{realSky ? 'Now follows the weather of your town, from Settings.' : 'Now needs a town in Settings › Look and sound; until then it stays clear.'}</p>
      </fieldset>
      <label className="pr-check">
        <input type="checkbox" checked={look.calm || systemCalm} disabled={systemCalm} onChange={(e) => onPick({ calm: e.target.checked })} /> Calm motion
        {systemCalm && <span className="pr-muted">(your system asks for it)</span>}
      </label>
    </aside>
  )
}
