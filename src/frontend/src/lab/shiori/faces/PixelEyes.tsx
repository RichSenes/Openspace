import { useRef } from 'react'
import { mixHex } from '../faceMachine'
import { BAYER4, EYE_W, eyeMask } from './faceGeometry'
import { fitCanvas, useFaceLoop, type FaceProps, type Frame } from './useFaceLoop'

/** Logical grid: 36 × 26 cells, scaled by a whole number like the building's sprites. */
const GW = 36
const GH = 26
const BG = '#14161c'
const WHITE = '#ece8df'
const OUTLINE = '#0b0c10'
const PUPIL = '#1b1e26'

const QUESTION = ['111', '001', '011', '000', '010']

/** Direction A: two pixel-art eyes in a bubble of dithered light. */
export function PixelEyes({ state, modeHex, reduced }: FaceProps) {
  const ref = useRef<HTMLCanvasElement>(null)

  useFaceLoop(
    state,
    (f: Frame) => {
      const canvas = ref.current
      if (!canvas) return
      const { w, h } = fitCanvas(canvas)
      const ctx = canvas.getContext('2d')
      if (!ctx) return
      const s = Math.max(1, Math.floor(Math.min(w / GW, h / GH)))
      const ox = Math.floor((w - GW * s) / 2)
      const oy = Math.floor((h - GH * s) / 2)
      ctx.clearRect(0, 0, w, h)
      const cell = (x: number, y: number, c: string) => {
        if (x < 0 || y < 0 || x >= GW || y >= GH) return
        ctx.fillStyle = c
        ctx.fillRect(ox + x * s, oy + y * s, s, s)
      }

      const { look, time } = f
      const calm = reduced ? 0 : 1
      const level = state === 'speaking' ? 0.5 + 0.5 * Math.sin(time * 9 * calm) * Math.sin(time * 3.3 * calm + 1) : 0
      const cx = GW / 2 - 0.5
      const cy = GH / 2 - 0.5

      // The bubble of light: an ordered dither that brightens with energy.
      const lo = mixHex(BG, look.color, 0.22)
      const hi = mixHex(BG, look.color, 0.48)
      const radius = 0.78 + 0.14 * look.energy + 0.08 * level
      const breathe = reduced ? 0 : Math.sin(time * 1.4 * look.speed) * 0.03
      for (let y = 0; y < GH; y++) {
        for (let x = 0; x < GW; x++) {
          const dx = (x - cx) / (GW / 2)
          const dy = (y - cy) / (GH / 2)
          const d = Math.hypot(dx, dy)
          const i = Math.max(0, 1 - d / (radius + breathe)) * (0.6 + 0.5 * look.energy)
          const t = BAYER4[y % 4][x % 4]
          if (i * 1.5 - 0.7 > t) cell(x, y, hi)
          else if (i * 1.4 > t) cell(x, y, lo)
          // A dotted ring in the mode's colour, turning slowly.
          const ring = Math.abs(d - (radius + breathe + 0.06))
          if (ring < 0.045 && (x + y + Math.floor(time * 3 * calm)) % 3 === 0) cell(x, y, mixHex(BG, modeHex, 0.75))
        }
      }

      // Lids, blinks and gaze per state.
      const period = state === 'listening' ? 5.5 : state === 'thinking' ? 3.2 : 4.2
      const phase = time % period
      const blink = phase < 0.09 ? 0 : phase < 0.16 ? 0.45 : 1
      const drift = reduced ? 0 : Math.sin(time * 0.7) * 0.35 * (state === 'idle' ? 1 : 0)
      const gx = Math.round((look.gazeX + drift) * 2)
      const gy = Math.round(look.gazeY * 2.4)
      const bob = state === 'speaking' && level > 0.62 ? -1 : 0

      const eyes: { x: number; open: number; slant: number }[] = [
        { x: cx - 6, open: 0.85, slant: 0 },
        { x: cx + 6, open: 0.85, slant: 0 },
      ]
      let shape: 'open' | 'happy' = 'open'
      let tall = 0
      if (state === 'listening') eyes.forEach((e) => ((e.open = 1), (tall = 2)))
      if (state === 'thinking') eyes.forEach((e) => (e.open = 0.55))
      if (state === 'speaking') eyes.forEach((e) => (e.open = 0.92))
      if (state === 'pleased') shape = 'happy'
      if (state === 'unsure') {
        eyes[0].open = 0.8
        eyes[0].slant = -0.35
        eyes[1].open = 0.55
        eyes[1].slant = 0.35
      }

      for (const e of eyes) {
        const mask = eyeMask(shape === 'happy' ? 1 : e.open * blink, shape, e.slant, tall)
        const h = mask.length
        const left = Math.round(e.x - EYE_W / 2 + 0.5)
        const top = Math.round(cy - h / 2 + 0.5) + bob
        const on = (x: number, y: number) => y >= 0 && y < h && x >= 0 && x < EYE_W && mask[y][x] === 1
        // Outline first, so the eye reads on any light.
        for (let y = -1; y <= h; y++)
          for (let x = -1; x <= EYE_W; x++)
            if (!on(x, y) && (on(x - 1, y) || on(x + 1, y) || on(x, y - 1) || on(x, y + 1))) cell(left + x, top + y, OUTLINE)
        for (let y = 0; y < h; y++) for (let x = 0; x < EYE_W; x++) if (on(x, y)) cell(left + x, top + y, shape === 'happy' ? WHITE : WHITE)
        if (shape === 'open') {
          // The pupil stays inside the eye with a cell of white around it.
          const px = Math.max(1, Math.min(EYE_W - 4, 3 + gx))
          const py = Math.round(h / 2 - 2 + gy)
          for (let y = 0; y < 4; y++)
            for (let x = 0; x < 3; x++) {
              const ax = Math.round(px) + x
              if (on(ax, py + y)) cell(left + ax, top + py + y, PUPIL)
            }
          if (on(Math.round(px), py)) cell(left + Math.round(px), top + py, WHITE)
        }
        if (state === 'pleased') {
          cell(left + 1, top + h / 2 + 3, '#d9677d')
          cell(left + EYE_W - 2, top + h / 2 + 3, '#d9677d')
        }
      }

      if (state === 'thinking') {
        for (let k = 0; k < 3; k++) {
          const a = time * 2.6 * look.speed * calm + (k * Math.PI * 2) / 3
          cell(Math.round(cx + Math.cos(a) * 5), Math.round(3 + Math.sin(a) * 1.2), mixHex(look.color, WHITE, 0.4))
        }
      }
      if (state === 'unsure') {
        QUESTION.forEach((row, y) => [...row].forEach((c, x) => c === '1' && cell(GW - 9 + x, 3 + y, look.color)))
      }
      if (state === 'pleased') {
        const on = reduced || Math.floor(time * 1.2) % 2 === 0
        const sp = on ? [[4, 4], [31, 6]] : [[6, 20], [29, 21]]
        for (const [x, y] of sp) {
          cell(x, y, WHITE)
          cell(x - 1, y, look.color)
          cell(x + 1, y, look.color)
          cell(x, y - 1, look.color)
          cell(x, y + 1, look.color)
        }
      }
    },
    12,
  )

  return <canvas ref={ref} className="face-canvas face-pixel" aria-hidden="true" />
}
