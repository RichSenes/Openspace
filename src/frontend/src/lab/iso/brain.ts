import { SQUASH } from './iso'
import { dot, glow } from './props'
import { mix, noise, Painter, rand } from './px'

/**
 * Shiori in the lab: a very large brain, drawn as pixel art is drawn (flat tones, dark furrow
 * lines, a lit edge on each fold), seen from the front and a little above, sunk in a well of
 * green light under a round glass dome. The well and the dome are traced a ray per pixel with
 * the room's own camera; signals of light run along the furrows, as between neurons.
 */

/** The camera: its elevation's sine and cosine. A point (X right, Y away, Z up) lands at (X, -Y·s - Z·c). */
const S = SQUASH
const C = Math.sqrt(1 - S * S)

/** Sizes in pixels. */
const WELL = 80
const LIP = 6
export const DOME_R = 92

const W = 2 * DOME_R + 16
const TOP = DOME_R + 8
const H = TOP + Math.ceil((DOME_R + LIP) * S) + 10

const GREEN = '#46ffb0'

type V = [number, number, number]
const dotv = (a: V, b: V) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
const norm = (a: V): V => {
  const l = Math.hypot(...a) || 1
  return [a[0] / l, a[1] / l, a[2] / l]
}

/** The view direction, into the scene and down. */
const DIR: V = [0, C, -S]

/** Where the ray behind a pixel starts: on the screen plane through the origin, then far back. */
function ray(sx: number, sy: number): V {
  return [sx, -sy * S - 400 * C, -sy * C + 400 * S]
}

function sphereHits(o: V, r: number): [number, number] | null {
  const B = 2 * dotv(o, DIR)
  const K = dotv(o, o) - r * r
  const disc = B * B - 4 * K
  if (disc < 0) return null
  const q = Math.sqrt(disc)
  return [(-B - q) / 2, (-B + q) / 2]
}

const along = (o: V, t: number): V => [o[0] + DIR[0] * t, o[1] + DIR[1] * t, o[2] + DIR[2] * t]

interface Sample {
  kind: 'lip' | 'well' | 'wall' | 'rim'
  p: V
  n: V
  g: number
  depth: number
}

function trace(sx: number, sy: number): Sample | null {
  const o = ray(sx, sy)
  // Where the ray meets the floor (Z = 0).
  const tf = o[2] / S
  const floor = along(o, tf)
  const r = Math.hypot(floor[0], floor[1])
  if (r < WELL) {
    // Into the well: its far wall, lit more the deeper it goes, then the light itself.
    const A = DIR[0] ** 2 + DIR[1] ** 2
    const B = 2 * (o[0] * DIR[0] + o[1] * DIR[1])
    const K = o[0] ** 2 + o[1] ** 2 - WELL * WELL
    const t = (-B + Math.sqrt(B * B - 4 * A * K)) / (2 * A)
    const p = along(o, t)
    return p[2] > -34 ? { kind: 'wall', p, n: [0, 0, 1], g: 0, depth: -p[2] } : { kind: 'well', p: floor, n: [0, 0, 1], g: 0, depth: 34 }
  }
  if (r < WELL + LIP) return { kind: 'lip', p: floor, n: [0, 0, 1], g: 0, depth: r - WELL }
  if (Math.abs(r - DOME_R) < 2.5) return { kind: 'rim', p: floor, n: [0, 0, 1], g: 0, depth: r - DOME_R }
  return null
}

const VIEW: V = [0, -C, S]

function paintWell(): HTMLCanvasElement {
  const p = new Painter(W, H)
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const s = trace(x + 0.5 - W / 2, y + 0.5 - TOP)
      if (!s) continue
      if (s.kind === 'wall') {
        // Ribs on the inside of the well, lit green from below.
        const ang = Math.atan2(s.p[1], s.p[0])
        const rib = Math.floor((ang / Math.PI) * 24) % 2 === 0
        const k = s.depth / 34
        p.set(x, y, mix(rib ? '#1b2a33' : '#24343e', GREEN, 0.15 + 0.6 * k * k))
      } else if (s.kind === 'well') {
        const d = Math.hypot(s.p[0], s.p[1]) / WELL
        p.set(x, y, d < 0.5 ? '#d9fff0' : d < 0.78 ? '#8cffd0' : GREEN)
      } else if (s.kind === 'lip') {
        const ang = Math.atan2(s.p[1], s.p[0])
        const light = s.depth > 2 && s.depth < 3.5 && Math.floor(((ang + Math.PI) / Math.PI) * 36) % 3 === 0
        p.set(x, y, light ? '#9ff3ff' : s.depth < 1.5 ? '#6b7788' : '#4a5466')
      } else {
        p.set(x, y, s.depth < 0 ? '#5d6878' : '#3a4352')
      }
    }
  }
  return p.canvas()
}

/** The brain's size, and how far its top stands above the floor point. */
const BW = 132
const BH = 124
const ABOVE = 96
/** Half its width and height: the outline is a rounded shape, fuller at the top. */
const RX = 64
const RY_TOP = 50
const RY_BOTTOM = 58
const MID = 56

/** Coral tones, darkest first: outline, furrow, shade, base, light, highlight, shine. */
const CORAL = ['#5e1124', '#8c2236', '#c9465a', '#ec6b7a', '#ff8f9c', '#ffb6be', '#ffe1e4']

export interface BrainArt {
  canvas: HTMLCanvasElement
  /** The furrow pixels, for the signals to run along. */
  furrows: Set<number>
}

/**
 * The brain, as in hand-made pixel art: a rounded outline split by the furrow between the halves,
 * folds as regions of a warped noise field whose borders are one-pixel furrow lines; each fold is
 * lit along its upper-left edge and shaded along its lower-right one, and the whole is shaded as a
 * ball lit from the upper left. Seen a little from above: the folds crowd towards the top. Where
 * it sinks into the well, it stops at the floor's curve and takes the green light.
 */
export function paintBrain(): BrainArt {
  const p = new Painter(BW, BH)
  const inside = (x: number, y: number) => {
    const dx = (x + 0.5 - BW / 2) / RX
    const dy = (y + 0.5 - MID) / (y + 0.5 < MID ? RY_TOP : RY_BOTTOM)
    // A little dip at the top in the middle, where the halves meet.
    const notch = Math.abs(dx) < 0.06 && dy < -0.93
    // Fuller at the top, a little narrower towards the bottom.
    const r = Math.abs(dx) ** 2.3 * (dy > 0 ? 1 + dy * 0.25 : 1) + Math.abs(dy) ** 2.2
    // Sunk in the well: nothing below the floor's front curve.
    const floorCurve = ABOVE + 24 * Math.sqrt(Math.max(0, 1 - ((x + 0.5 - BW / 2) / 64) ** 2))
    return r <= 1 && !notch && y + 0.5 < floorCurve
  }
  const ins = new Uint8Array(BW * BH)
  for (let y = 0; y < BH; y++) for (let x = 0; x < BW; x++) if (inside(x, y)) ins[y * BW + x] = 1
  const id = (x: number, y: number) => (x < 0 || y < 0 || x >= BW || y >= BH || !ins[y * BW + x] ? -1 : 0)
  // The furrows are grown, not computed: winding lines that start at the edge or between folds,
  // turn a little at each step, branch now and then, and stop before touching another line.
  // What is left between them are the folds, of even width, as in a real brain.
  const furrow = new Uint8Array(BW * BH)
  const owner = new Int32Array(BW * BH).fill(-1)
  const rnd = rand(29)
  const GAP = 5
  const free = (x: number, y: number, me: number) => {
    if (x < 1 || y < 1 || x >= BW - 1 || y >= BH - 1 || !ins[y * BW + x]) return false
    // The halves stay apart: no line crosses the middle furrow.
    if (Math.abs(x + 0.5 - BW / 2) < 3) return false
    for (let dy = -GAP; dy <= GAP; dy++) {
      for (let dx = -GAP; dx <= GAP; dx++) {
        if (dx * dx + dy * dy > GAP * GAP + 1) continue
        const k = (y + dy) * BW + x + dx
        if (k < 0 || k >= BW * BH || !furrow[k]) continue
        // Its own last few pixels do not count.
        if (owner[k] === me && recent.has(k)) continue
        return false
      }
    }
    return true
  }
  let recent = new Set<number>()
  let walkers = 0
  const grow = (x0: number, y0: number, angle: number, budget: number) => {
    const me = walkers++
    let x = x0
    let y = y0
    let th = angle
    let turn = (rnd() - 0.5) * 0.25
    recent = new Set<number>()
    const trail: number[] = []
    for (let step = 0; step < budget; step++) {
      th += turn + (rnd() - 0.5) * 0.3
      turn = Math.max(-0.35, Math.min(0.35, turn + (rnd() - 0.5) * 0.15))
      const nx = x + Math.cos(th)
      const ny = y + Math.sin(th)
      const px = Math.round(nx)
      const py = Math.round(ny)
      const k = py * BW + px
      if (k !== Math.round(y) * BW + Math.round(x)) {
        if (!free(px, py, me)) {
          // Blocked by another furrow: run on straight into it, so lines meet as they do in a brain.
          const path: number[] = []
          let jx = x
          let jy = y
          for (let n = 0; n <= GAP + 1; n++) {
            jx += Math.cos(th)
            jy += Math.sin(th)
            const q = Math.round(jy) * BW + Math.round(jx)
            if (!ins[q]) break
            if (furrow[q] && !recent.has(q)) {
              for (const m of path) furrow[m] = 1
              break
            }
            path.push(q)
          }
          break
        }
        furrow[k] = 1
        owner[k] = me
        trail.push(k)
        recent.add(k)
        if (trail.length > 9) recent.delete(trail[trail.length - 10])
      }
      x = nx
      y = ny
      // Now and then a branch, off to one side.
      if (step > 6 && rnd() < 0.05) {
        const saved = recent
        grow(x, y, th + (rnd() < 0.5 ? 1 : -1) * (1.2 + rnd() * 0.5), budget - step)
        recent = saved
      }
    }
  }
  // From the edge inwards first, then from anywhere left open.
  for (let k = 0; k < 140; k++) {
    const a = rnd() * Math.PI * 2
    let x = BW / 2 + Math.cos(a) * RX
    let y = MID + Math.sin(a) * RY_TOP
    for (let n = 0; n < 80 && !(ins[Math.round(y) * BW + Math.round(x)] ?? 0); n++) {
      x += (BW / 2 - x) * 0.04
      y += (MID - y) * 0.04
    }
    grow(x, y, Math.atan2(MID - y, BW / 2 - x) + (rnd() - 0.5) * 1.2, 14 + Math.floor(rnd() * 34))
  }
  for (let k = 0; k < 2500; k++) {
    const x = 2 + Math.floor(rnd() * (BW - 4))
    const y = 2 + Math.floor(rnd() * (BH - 4))
    recent = new Set<number>()
    if (free(x, y, -2)) grow(x, y, rnd() * Math.PI * 2, 12 + Math.floor(rnd() * 30))
  }
  // Two pixels thick, as the lines of hand-made pixel art at this size.
  const thin = furrow.slice()
  for (let y = 1; y < BH; y++) for (let x = 1; x < BW; x++) if (ins[y * BW + x] && (thin[y * BW + x - 1] || thin[(y - 1) * BW + x])) furrow[y * BW + x] = 1
  const isFurrow = (x: number, y: number) => x >= 0 && y >= 0 && x < BW && y < BH && furrow[y * BW + x] === 1
  const furrows = new Set<number>()
  for (let y = 0; y < BH; y++) {
    for (let x = 0; x < BW; x++) {
      if (id(x, y) < 0) continue
      const dx = (x + 0.5 - BW / 2) / RX
      const dy = (y + 0.5 - MID) / RY_TOP
      // The ball's light: brighter up and left, darker down and right, in whole tones.
      const ball = Math.max(-1, Math.min(1, -0.55 * dx - 0.65 * dy + 0.35 * Math.sqrt(Math.max(0, 1 - dx * dx - dy * dy))))
      let tone = 3 + Math.round(ball * 1.1)
      if (Math.abs(x + 0.5 - BW / 2) < 1.5) {
        tone = 1
      } else if (isFurrow(x, y)) {
        tone = 1
        if (thin[y * BW + x]) furrows.add(y * BW + x)
      } else {
        // The fold's own relief: lit just below or right of a furrow above-left, shaded just above one below-right.
        const litEdge = isFurrow(x, y - 1) || isFurrow(x - 1, y) || isFurrow(x, y - 2) || isFurrow(x - 2, y)
        const darkEdge = isFurrow(x, y + 1) || isFurrow(x + 1, y)
        if (litEdge && !darkEdge) tone += 1
        if (darkEdge && !litEdge) tone -= 1
        tone = Math.max(2, Math.min(5, tone))
      }
      let c = CORAL[tone]
      // A glint on the brightest folds.
      if (tone === 5 && ball > 0.55 && noise(x / 3, y / 3, 41) > 0.72) c = CORAL[6]
      // Green light from the well on what is near the floor.
      const floorCurve = ABOVE + 24 * Math.sqrt(Math.max(0, 1 - ((x + 0.5 - BW / 2) / 64) ** 2))
      const near = floorCurve - (y + 0.5)
      if (near < 4) c = mix(c, GREEN, 0.55)
      else if (near < 9) c = mix(c, GREEN, 0.25)
      p.set(x, y, c)
    }
  }
  p.outline(CORAL[0])
  return { canvas: p.canvas(), furrows }
}

/** The glass: faint where it faces you, bright at its edges, a highlight where the light hits. */
function paintGlass(): HTMLCanvasElement {
  const p = new Painter(W, H)
  const L = norm([-0.6, -0.5, 0.65])
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const o = ray(x + 0.5 - W / 2, y + 0.5 - TOP)
      const hits = sphereHits(o, DOME_R)
      if (!hits) continue
      let alpha = 0
      let color = '#d8f6ff'
      for (const t of hits) {
        const q = along(o, t)
        if (q[2] < 0) continue
        const n = norm(q)
        const facing = Math.abs(dotv(n, VIEW))
        alpha += 0.05 + 0.6 * (1 - facing) ** 4
        const k = 2 * dotv(n, L)
        const spec = Math.max(0, dotv([k * n[0] - L[0], k * n[1] - L[1], k * n[2] - L[2]], VIEW)) ** 220
        if (spec > 0.35 && t === hits[0]) {
          alpha = 0.85
          color = '#ffffff'
        }
      }
      if (alpha > 0.02) p.set(x, y, color, Math.min(0.9, alpha))
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
  /** Paints her, centred on the floor point (x, y). */
  draw(ctx: CanvasRenderingContext2D, x: number, y: number, t: number): void
}

/** A signal running along the furrows, from neuron to neuron. */
interface Signal {
  at: number
  from: number
  trail: number[]
  steps: number
  carry: number
}

export function makeBrain(): Brain {
  const well = paintWell()
  const glass = paintGlass()
  const { canvas: brain, furrows } = paintBrain()
  const list = [...furrows]
  const r = rand(17)
  const particles: Particle[] = Array.from({ length: 26 }, (_, k) => ({
    r: 70 + r() * 16,
    tilt: 0.2 + r() * 1.1,
    yaw: r() * Math.PI * 2,
    speed: (3.5 + r() * 3) * (k % 4 === 0 ? -1 : 1),
    ph: r() * Math.PI * 2,
    color: ['#9ff3ff', '#ffffff', '#b8ffe0'][k % 3],
    big: false,
  }))
  const pick = () => list[Math.floor(r() * list.length)]
  const signals: Signal[] = Array.from({ length: 5 }, () => ({ at: pick(), from: -1, trail: [], steps: 0, carry: 0 }))
  // Neighbours along a furrow, never straight back.
  const next = (s: Signal) => {
    const x = s.at % BW
    const y = Math.floor(s.at / BW)
    const options: number[] = []
    for (const [dx, dy] of [
      [1, 0],
      [-1, 0],
      [0, 1],
      [0, -1],
      [1, 1],
      [-1, -1],
      [1, -1],
      [-1, 1],
    ]) {
      const k = (y + dy) * BW + x + dx
      if (furrows.has(k) && k !== s.from && !s.trail.includes(k)) options.push(k)
    }
    return options.length ? options[Math.floor(r() * options.length)] : -1
  }
  let last = 0

  return {
    draw(ctx, x, y, t) {
      const dt = Math.max(0, Math.min(0.1, t - last))
      last = t
      const ox = Math.round(x - W / 2)
      const oy = Math.round(y - TOP)
      const bx = Math.round(x - BW / 2)
      const by = Math.round(y - ABOVE)
      const placed = particles.map((p) => place(p, t))
      for (const p of placed) if (p.far) trail(ctx, x, y, p)
      ctx.drawImage(well, ox, oy)
      // The light of the well, breathing, behind and under her.
      glow(ctx, x, y + 6, 76, GREEN, 0.32 + 0.08 * Math.sin(t * 1.6))
      ctx.drawImage(brain, bx, by)
      // Signals: a bright point with a short tail, a little flash where one starts.
      for (const s of signals) {
        s.carry += dt * 34
        while (s.carry >= 1) {
          s.carry -= 1
          const n = next(s)
          s.trail.unshift(s.at)
          s.trail.length = Math.min(s.trail.length, 5)
          s.steps++
          if (n < 0 || s.steps > 60 + (s.at % 40)) {
            s.from = -1
            s.at = pick()
            s.trail = []
            s.steps = 0
          } else {
            s.from = s.at
            s.at = n
          }
        }
        if (s.steps < 4) glow(ctx, bx + (s.at % BW), by + Math.floor(s.at / BW), 7, '#bff8ff', 0.5)
        s.trail.forEach((k, i) => dot(ctx, bx + (k % BW), by + Math.floor(k / BW), '#9ff3ff', 0.7 - i * 0.13))
        dot(ctx, bx + (s.at % BW), by + Math.floor(s.at / BW), '#ffffff')
      }
      for (const p of placed) if (!p.far) trail(ctx, x, y, p)
      ctx.drawImage(glass, ox, oy)
    },
  }
}

/** A point of an orbit around her, tilted and turned; Y away from you, Z up. */
function orbit(p: Particle, th: number): V {
  const x0 = Math.cos(th) * p.r
  const y0 = Math.sin(th) * p.r
  const z1 = y0 * Math.sin(p.tilt)
  const y1 = y0 * Math.cos(p.tilt)
  return [x0 * Math.cos(p.yaw) - y1 * Math.sin(p.yaw), x0 * Math.sin(p.yaw) + y1 * Math.cos(p.yaw), 22 + z1]
}

function place(p: Particle, t: number) {
  const th = p.ph + t * p.speed
  const q = orbit(p, th)
  return { p, th, far: q[1] * C - q[2] * S > 0 }
}

/** A particle and its short fading trail; under the floor it is lost in the light. */
function trail(ctx: CanvasRenderingContext2D, cx: number, cy: number, placed: { p: Particle; th: number }) {
  const { p, th } = placed
  const step = 0.045 * Math.sign(p.speed)
  for (let k = 5; k >= 0; k--) {
    const q = orbit(p, th - k * step)
    if (q[2] < 0) continue
    const sx = cx + q[0]
    const sy = cy - q[1] * S - q[2] * C
    dot(ctx, sx, sy, p.color, k === 0 ? 1 : 0.7 * (1 - k / 9), k === 0 && p.big ? 2 : 1)
    if (k === 0) glow(ctx, sx, sy, 3, p.color, 0.35)
  }
}
