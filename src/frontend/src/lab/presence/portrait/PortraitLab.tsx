import { useEffect, useRef, useState } from 'react'
import { FACE_LOOKS, FACE_STATES, type FaceState } from '../../shiori/faceMachine'
import { rng } from '../body/shapes'
import { EXPRESSIONS, expressionOf, makeFace, stepFace } from './face'
import { NEUTRAL } from './expression'
import { drawPortrait, PORTRAIT_H, PORTRAIT_W } from './portrait'

/** One still of a state's expression, at rest. */
function Still({ state }: { state: FaceState }) {
  const ref = useRef<HTMLCanvasElement>(null)
  useEffect(() => {
    const c = ref.current!
    c.width = PORTRAIT_W
    c.height = PORTRAIT_H
    drawPortrait(c.getContext('2d', { willReadFrequently: true })!, { ...NEUTRAL, ...EXPRESSIONS[state], open: state === 'speaking' ? 0.6 : (EXPRESSIONS[state].open ?? 0) }, 0)
  }, [state])
  return (
    <figure style={{ margin: 0 }}>
      <canvas ref={ref} style={{ width: PORTRAIT_W * 2, height: PORTRAIT_H * 2, imageRendering: 'pixelated' }} />
      <figcaption style={{ textAlign: 'center', fontFamily: 'var(--mono)', fontSize: 12 }}>{FACE_LOOKS[state].label}</figcaption>
    </figure>
  )
}

/** The face alive: springs, blinks, glances, breath and speech, in colour. */
function Live({ state }: { state: FaceState }) {
  const ref = useRef<HTMLCanvasElement>(null)
  const live = useRef(state)
  useEffect(() => {
    live.current = state
  })
  useEffect(() => {
    const c = ref.current!
    c.width = PORTRAIT_W
    c.height = PORTRAIT_H
    const g = c.getContext('2d', { willReadFrequently: true })!
    const r = rng(7)
    let face = makeFace('idle')
    let last = performance.now()
    let raf = 0
    const frame = (now: number) => {
      raf = requestAnimationFrame(frame)
      const dt = Math.min(0.05, (now - last) / 1000)
      last = now
      face = stepFace(face, dt, live.current, r, false)
      drawPortrait(g, expressionOf(face), face.time)
      c.style.transform = `translateY(${-face.breath * 4}px)`
    }
    raf = requestAnimationFrame(frame)
    return () => cancelAnimationFrame(raf)
  }, [])
  return <canvas ref={ref} style={{ width: PORTRAIT_W * 4, height: PORTRAIT_H * 4, imageRendering: 'pixelated' }} />
}

/** `?lab=portrait`: Shiori's face alive, and a still of every expression. */
export function PortraitLab() {
  const [state, setState] = useState<FaceState>('idle')
  return (
    <div style={{ minHeight: '100dvh', background: '#07060d', color: '#ecebff', display: 'grid', gap: 24, padding: 24, justifyItems: 'center' }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 24, alignItems: 'center', justifyContent: 'center' }}>
        <Live state={state} />
        <div style={{ display: 'grid', gap: 8 }} role="radiogroup" aria-label="State">
          {FACE_STATES.map((s) => (
            <button
              key={s}
              type="button"
              role="radio"
              aria-checked={state === s}
              onClick={() => setState(s)}
              style={{ minHeight: 34, padding: '0 14px', border: `1px solid ${state === s ? '#82a8f5' : '#352a4d'}`, background: 'transparent', color: state === s ? '#82a8f5' : '#ecebff', font: 'inherit', cursor: 'pointer', textAlign: 'left' }}
            >
              {FACE_LOOKS[s].label}
            </button>
          ))}
        </div>
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, justifyContent: 'center' }}>
        {FACE_STATES.map((s) => (
          <Still key={s} state={s} />
        ))}
      </div>
    </div>
  )
}
