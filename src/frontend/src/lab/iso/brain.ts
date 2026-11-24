import { cylinder } from './iso'
import { dot, glow } from './props'
import { mix, noise, Painter, rand } from './px'

/**
 * Shiori in the lab: a very large brain floating under a glass dome, its folds lit by waves of
 * thought, particles orbiting it fast with short trails. Painted in layers so the particles pass
 * both behind and in front of it.
 */

const RAMP = ['#2a1438', '#46205a', '#6a2f78', '#94478f', '#bf68a6', '#e095bd', '#f7c8db']
const SCALE = 1.12
const BW = Math.round(132 * SCALE)
const BH = Math.round(98 * SCALE)

export const DOME_RX = 92
const DOME_H = 172
const RIM_H = 10

interface Fold {
  x: number
  y: number
}

function paintBrain(): { canvas: HTMLCanvasElement; folds: Fold[] } {
  // Seen from the side and a little above, as everybody pictures a brain: the cerebrum, the
  // temporal lobe under it, the cerebellum at the back, the stem going down into the cables.
  const p = new Painter(BW, BH)
  const folds: Fold[] = []
  const L = [-0.55, -0.6, 0.58]
  const n = Math.hypot(...L)
  const light = L.map((v) => v / n)
  const ell = (x: number, y: number, cx: number, cy: number, rx: number, ry: number) => ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2
  for (let y = 0; y < BH; y++) {
    for (let x = 0; x < BW; x++) {
      const px = (x + 0.5) / SCALE
      const py = (y + 0.5) / SCALE
      const cer = ell(px, py, 64, 42, 58, py < 42 ? 38 : 33)
      const tem = ell(px, py, 52, 60, 34, 17)
      const cbl = ell(px, py, 98, 70, 22, 13)
      const stem = py > 70 && py < 97 && px > 82 + (py - 70) * 0.12 && px < 95 - (py - 70) * 0.12
      let region: 'cer' | 'tem' | 'cbl' | 'stem' | null = null
      if (cer <= 1) region = 'cer'
      else if (tem <= 1) region = 'tem'
      else if (cbl <= 1) region = 'cbl'
      else if (stem) region = 'stem'
      if (!region) continue
      const [cx, cy, rx, ry, e] =
        region === 'cer' ? [64, 42, 58, 38, cer] : region === 'tem' ? [52, 60, 34, 17, tem] : region === 'cbl' ? [98, 70, 22, 13, cbl] : [88, 84, 7, 14, Math.min(1, ((px - 88) / 7) ** 2)]
      const nx = (px - cx) / rx
      const ny = (py - cy) / ry
      const nz = Math.sqrt(Math.max(0, 1 - e))
      let lit = Math.max(0, -(nx * light[0] + ny * light[1]) * 0.75 + nz * light[2] * 1.05)
      lit = lit * 0.78 + 0.14
      let step = Math.min(RAMP.length - 1, Math.max(0, Math.floor(lit * RAMP.length)))
      let groove = false
      let ridge = false
      if (region === 'cer' || region === 'tem') {
        // Gyri: the contour lines of a warped noise field, a dark groove under a lit ridge.
        const wx = px / 7.5 + 0.7 * noise(px / 11, py / 11, 3) + (region === 'tem' ? 40 : 0)
        const wy = py / 7.5 + 0.7 * noise(px / 11 + 7, py / 11, 4)
        const f = noise(wx, wy, 11)
        const band = (f * 3.2) % 1
        groove = Math.abs(band - 0.5) < 0.09
        // Each gyrus is rounded: lit on the side towards the light, shaded on the other.
        step += Math.round(Math.cos((band - 0.15) * Math.PI * 2) * 0.7)
        ridge = false
        // The big furrows: between the halves (the arc on top), across the side, down the middle.
        const top = Math.abs(px - 70) < 52 ? 42 - 27 * Math.sqrt(1 - ((px - 70) / 52) ** 2) : -99
        if (region === 'cer' && Math.abs(py - top) < 1.2) step = 0
        else if (region === 'cer' && py < top) step = Math.max(0, step - 1)
        const sylvian = 60 - (px - 28) * 0.22
        if (px > 28 && px < 96 && Math.abs(py - sylvian) < 1.1) groove = true
        const central = 70 - (py - 16) * 0.18
        if (py > 14 && py < 52 && Math.abs(px - central - Math.sin(py * 0.4) * 1.5) < 0.9 && region === 'cer') groove = true
      } else if (region === 'cbl') {
        groove = Math.abs(((py + (px - 98) * 0.12) % 3) - 1.5) < 0.5
        step = Math.max(0, step - 1)
      } else {
        step = Math.max(0, step - 2)
      }
      if (groove) step = Math.max(0, step - 2)
      if (ridge) step = Math.min(RAMP.length - 1, step + 1)
      if (nz < 0.22) step = Math.max(0, step - 1)
      step = Math.min(RAMP.length - 1, Math.max(0, step))
      p.set(x, y, RAMP[step])
      if (groove && region !== 'stem') folds.push({ x, y })
    }
  }
  // A few cold lights, as if the folds were wired.
  const r = rand(5)
  for (let k = 0; k < 46; k++) {
    const f = folds[Math.floor(r() * folds.length)]
    p.set(f.x, f.y, mix('#7ff0ff', RAMP[2], 0.4))
  }
  p.outline('#1c0c26')
  return { canvas: p.canvas(), folds }
}

function paintRim(): HTMLCanvasElement {
  const w = DOME_RX * 2 + 24
  const h = DOME_RX + RIM_H + 20
  const p = new Painter(w, h)
  const cx = w / 2
  const cy = DOME_RX / 2 + RIM_H + 6
  p.ellipse(cx + 4, cy + 3, DOME_RX + 8, (DOME_RX + 8) / 2, '#0b1220', 0.2)
  cylinder(p, cx, cy, DOME_RX + 4, RIM_H, '#4a5466', '#646f81')
  // Inside the dome: dark glass with a grid.
  p.ellipse(cx, cy - RIM_H, DOME_RX - 2, (DOME_RX - 2) / 2, (x, y) => ((x - Math.round(cx)) % 8 === 0 || ((y - Math.round(cy)) * 2) % 8 === 0 ? '#27465a' : '#152230'))
  p.ellipse(cx, cy - RIM_H, 16, 8, '#2b3443')
  p.ellipse(cx, cy - RIM_H - 1, 12, 6, '#3d4a5c')
  // Lights around the rim's front.
  for (let k = 0; k <= 24; k++) {
    const th = Math.PI * (k / 24)
    p.set(cx + Math.cos(th) * (DOME_RX + 3), cy - RIM_H / 2 + Math.sin(th) * ((DOME_RX + 3) / 2), k % 3 === 0 ? '#9ff3ff' : '#3cc7e0')
  }
  p.outline('#18202b')
  return p.canvas()
}

function paintGlass(): HTMLCanvasElement {
  const w = DOME_RX * 2 + 4
  const h = DOME_H + DOME_RX / 2 + 4
  const p = new Painter(w, h)
  const cx = w / 2
  const base = DOME_H + 2
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const dx = (x + 0.5 - cx) / DOME_RX
      if (Math.abs(dx) > 1) continue
      const top = base - DOME_H * Math.sqrt(1 - dx * dx)
      const bottom = base + (DOME_RX / 2) * Math.sqrt(1 - dx * dx)
      if (y + 0.5 < top || y + 0.5 > bottom) continue
      const fromEdge = Math.min(y + 0.5 - top, Math.abs(Math.abs(dx) - 1) * DOME_RX)
      // Faint glass, a brighter edge, a long highlight on the upper left.
      let a = 0.09
      let c = '#cdefff'
      if (fromEdge < 1.5) {
        a = 0.75
        c = '#e4f9ff'
      } else if (fromEdge < 4) a = 0.22
      // A tall curved streak on the left and a shorter one on the right: light on round glass.
      const streak = Math.abs(dx + 0.62 - 0.12 * ((y - (base - DOME_H)) / DOME_H)) < 0.035
      if (streak && y > base - DOME_H * 0.85 && y < base - DOME_H * 0.15) {
        a = 0.42
        c = '#ffffff'
      }
      if (Math.abs(dx - 0.7) < 0.025 && y > base - DOME_H * 0.55 && y < base - DOME_H * 0.2) {
        a = 0.3
        c = '#ffffff'
      }
      p.set(x, y, c, a)
    }
  }
  return p.canvas()
}

interface Particle {
  r: number
  tilt: number
  yaw: number
  speed: number
  ph: number
  color: string
  big: boolean
}

export interface Brain {
  /** Paints the whole of her, standing at the floor point (x, y). */
  draw(ctx: CanvasRenderingContext2D, x: number, y: number, t: number): void
}

export function makeBrain(): Brain {
  const { canvas: brain, folds } = paintBrain()
  const rim = paintRim()
  const glass = paintGlass()
  const r = rand(17)
  const particles: Particle[] = Array.from({ length: 90 }, (_, k) => ({
    r: 72 + r() * 16,
    tilt: 0.25 + r() * 1.1,
    yaw: r() * Math.PI * 2,
    speed: (4 + r() * 4) * (k % 5 === 0 ? -1 : 1),
    ph: r() * Math.PI * 2,
    color: ['#9ff3ff', '#ffffff', '#d8a6ff', '#7ff0ff', '#ffd6f0'][k % 5],
    big: r() > 0.8,
  }))

  return {
    draw(ctx, x, y, t) {
      const bob = Math.round(Math.sin(t * 1.3) * 2)
      const bx = x
      const by = y - RIM_H - 70 + bob
      ctx.drawImage(rim, Math.round(x - rim.width / 2), Math.round(y - (DOME_RX / 2 + RIM_H + 6)))
      // Cables from the floor of the dome up into her.
      ctx.fillStyle = '#2b3443'
      for (const [dx, w] of [
        [22, 4],
        [28, 4],
        [34, 4],
      ] as const)
        ctx.fillRect(Math.round(x + dx), Math.round(by + 40), w, Math.round(y - RIM_H - (by + 40)))
      for (let k = 0; k < 3; k++) {
        const ph = (t * 0.8 + k / 3) % 1
        dot(ctx, x + 23 + k * 6, y - RIM_H - ph * (y - RIM_H - by - 40), '#9ff3ff', 1 - ph * 0.4, 1)
      }
      glow(ctx, bx, by, 110, '#b25cff', 0.22 + 0.06 * Math.sin(t * 1.7))
      const sorted = particles.map((p) => position(p, t))
      for (const p of sorted) if (p.depth < 0) trail(ctx, bx, by, p)
      ctx.drawImage(brain, Math.round(bx - BW / 2), Math.round(by - BH / 2))
      // Waves of thought along the folds.
      for (let w = 0; w < 3; w++) {
        const ph = (t * 0.45 + w / 3) % 1
        const ox = BW / 2 + Math.cos(w * 2.1 + Math.floor(t * 0.45 + w / 3)) * 30
        const oy = BH / 2 + Math.sin(w * 1.7 + Math.floor(t * 0.45 + w / 3)) * 18
        const radius = ph * 90
        for (const f of folds) {
          const d = Math.hypot(f.x - ox, f.y - oy)
          if (Math.abs(d - radius) < 1.3) dot(ctx, bx - BW / 2 + f.x, by - BH / 2 + f.y, '#9ff3ff', 0.9 * (1 - ph))
        }
      }
      for (const p of sorted) if (p.depth >= 0) trail(ctx, bx, by, p)
      glow(ctx, bx, by + 40, 70, '#5fd7ff', 0.12)
      ctx.drawImage(glass, Math.round(x - glass.width / 2), Math.round(y - RIM_H - DOME_H - 2))
    },
  }
}

interface Placed {
  p: Particle
  depth: number
  t: number
}

function point(p: Particle, th: number): [number, number, number] {
  // A circle tilted about the x axis, then turned about the vertical.
  const x0 = Math.cos(th) * p.r
  const z0 = Math.sin(th) * p.r
  const y1 = -z0 * Math.sin(p.tilt)
  const z1 = z0 * Math.cos(p.tilt)
  const x = x0 * Math.cos(p.yaw) - z1 * Math.sin(p.yaw)
  const z = x0 * Math.sin(p.yaw) + z1 * Math.cos(p.yaw)
  return [x, y1 * 0.9 + z * 0.42, z]
}

function position(p: Particle, t: number): Placed {
  const th = p.ph + t * p.speed
  return { p, depth: point(p, th)[2], t: th }
}

/** A particle and the short fading trail it leaves. */
function trail(ctx: CanvasRenderingContext2D, cx: number, cy: number, placed: Placed) {
  const { p, t } = placed
  const step = 0.045 * Math.sign(p.speed)
  for (let k = 7; k >= 0; k--) {
    const [x, y] = point(p, t - k * step)
    dot(ctx, cx + x, cy + y, p.color, k === 0 ? 1 : 0.7 * (1 - k / 9), k === 0 && p.big ? 2 : 1)
    if (k === 0) glow(ctx, cx + x, cy + y, 4, p.color, 0.5)
  }
}
