import { useEffect } from 'react'
import { MODE_ICONS } from './icons'
import { MODE_ORDER, MODES, type Mode } from './modes'

/**
 * The three modes as one segmented control: a sliding thumb, an icon and a word each, the power
 * of each mode as pips, and what the current one allows said underneath. Alt+1/2/3 switch.
 */
export function ModeSwitch({ mode, onSwitch }: { mode: Mode; onSwitch: (m: Mode) => void }) {
  const index = MODE_ORDER.indexOf(mode)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!e.altKey) return
      const n = Number(e.key)
      if (n >= 1 && n <= 3) {
        e.preventDefault()
        onSwitch(MODE_ORDER[n - 1])
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onSwitch])

  const move = (delta: number) => onSwitch(MODE_ORDER[(index + delta + 3) % 3])

  return (
    <div className="mode">
      <div
        className="mode-seg"
        role="radiogroup"
        aria-label="Session mode"
        style={{ '--i': index } as React.CSSProperties}
        onKeyDown={(e) => {
          const delta = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0
          if (delta) {
            e.preventDefault()
            move(delta)
          }
        }}
      >
        <span className="mode-thumb" aria-hidden="true">
          <svg key={mode} className="mode-scribble" viewBox="0 0 120 40" preserveAspectRatio="none">
            <path pathLength={1} d="M8 22 C 10 6, 108 4, 113 18 C 117 32, 18 38, 6 27 C 2 20, 20 9, 46 8" />
          </svg>
        </span>
        {MODE_ORDER.map((m, i) => {
          const Icon = MODE_ICONS[m]
          const on = m === mode
          return (
            <button
              key={m}
              type="button"
              role="radio"
              aria-checked={on}
              tabIndex={on ? 0 : -1}
              className={`mode-opt mode-${m}${on ? ' is-on' : ''}`}
              onClick={() => onSwitch(m)}
              title={`${MODES[m].label} (Alt+${i + 1})`}
            >
              <Icon size={16} strokeWidth={2} aria-hidden="true" />
              <span className="mode-label">{MODES[m].label}</span>
              <span className="mode-pips" aria-label={`power ${MODES[m].power} of 3`}>
                {[1, 2, 3].map((p) => (
                  <i key={p} className={p <= MODES[m].power ? 'is-full' : undefined} />
                ))}
              </span>
            </button>
          )
        })}
      </div>
      <p className="mode-allows" key={mode} aria-live="polite">
        <strong>{MODES[mode].label}.</strong> {MODES[mode].allows}
      </p>
    </div>
  )
}
