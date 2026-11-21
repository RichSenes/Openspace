import { useEffect, useRef, useState } from 'react'
import { blendLook, FACE_LOOKS, type FaceLook, type FaceState } from '../faceMachine'

export interface FaceProps {
  state: FaceState
  /** Hex colour of the current mode: the face takes it quietly. */
  modeHex: string
  reduced: boolean
  /** Small header size on phones. */
  compact: boolean
}

export interface Frame {
  /** Seconds since the face started. */
  time: number
  dt: number
  look: FaceLook
  /** Seconds since the state last changed. */
  inState: number
  state: FaceState
}

/**
 * Runs `draw` every animation frame with the look eased toward the current state's.
 * `fps` caps the rate for faces that should move in steps, like pixel art.
 */
export function useFaceLoop(state: FaceState, draw: (f: Frame) => void, fps = 60) {
  const target = useRef(state)
  const changedAt = useRef(0)
  const drawRef = useRef(draw)
  useEffect(() => {
    drawRef.current = draw
  })
  useEffect(() => {
    target.current = state
    changedAt.current = performance.now()
  }, [state])

  useEffect(() => {
    let raf = 0
    let last = performance.now()
    let lastDraw = 0
    const start = last
    let look = FACE_LOOKS[target.current]
    const tick = (now: number) => {
      raf = requestAnimationFrame(tick)
      const dt = Math.min(0.1, (now - last) / 1000)
      last = now
      look = blendLook(look, FACE_LOOKS[target.current], dt * 4)
      if (now - lastDraw < 1000 / fps - 2) return
      const frameDt = (now - lastDraw) / 1000
      lastDraw = now
      drawRef.current({
        time: (now - start) / 1000,
        dt: Math.min(0.2, frameDt),
        look,
        inState: (now - changedAt.current) / 1000,
        state: target.current,
      })
    }
    raf = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(raf)
  }, [fps])
}

export function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(() => matchMedia('(prefers-reduced-motion: reduce)').matches)
  useEffect(() => {
    const mq = matchMedia('(prefers-reduced-motion: reduce)')
    const on = () => setReduced(mq.matches)
    mq.addEventListener('change', on)
    return () => mq.removeEventListener('change', on)
  }, [])
  return reduced
}

/** Keeps a canvas's backing store at the size it is shown, in device pixels. */
export function fitCanvas(canvas: HTMLCanvasElement): { w: number; h: number; dpr: number } {
  const dpr = Math.min(2, window.devicePixelRatio || 1)
  const w = Math.max(1, Math.round(canvas.clientWidth * dpr))
  const h = Math.max(1, Math.round(canvas.clientHeight * dpr))
  if (canvas.width !== w || canvas.height !== h) {
    canvas.width = w
    canvas.height = h
  }
  return { w, h, dpr }
}
