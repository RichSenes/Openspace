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
/** How far below the floor the glowing liquid stands. */
const LIQUID = 4
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
    if (p[2] > -LIQUID) return { kind: 'wall', p, n: [0, 0, 1], g: 0, depth: -p[2] }
    // The liquid, filled almost to the brim.
    return { kind: 'well', p: along(o, (o[2] + LIQUID) / S), n: [0, 0, 1], g: 0, depth: LIQUID }
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
        p.set(x, y, mix(rib ? '#2a3a44' : '#34464f', GREEN, 0.35 + (0.4 * s.depth) / LIQUID))
      } else if (s.kind === 'well') {
        // Glowing liquid: brightest round her, with caustic veins of light.
        const d = Math.hypot(s.p[0], s.p[1]) / WELL
        const vein = Math.abs(noise(s.p[0] / 11, s.p[1] / 7, 61) - 0.5) < 0.035
        p.set(x, y, vein ? '#d9fff0' : d < 0.62 ? '#8cffd0' : d < 0.86 ? GREEN : '#2fd894')
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

/** The brain's size; the floor point is ABOVE pixels under its top, she sinks to about half. */
const BW = 140
const BH = 126
const ABOVE = 44
/** How far the waterline comes towards you in the middle (the liquid cuts her as a curve). */
const FRONT = 27
const RX = 66

/** Coral tones, darkest first: outline, furrow, shade, base, light, highlight, shine. */
const CORAL = ['#5e1124', '#8c2236', '#c9465a', '#ec6b7a', '#ff8f9c', '#ffb6be', '#ffe1e4']
const DEEP = '#0f7a55'

export interface BrainArt {
  canvas: HTMLCanvasElement
  /** The furrow pixels above the liquid, for the signals to run along. */
  furrows: Set<number>
}

/** Where the liquid meets her, in her own pixels: a curve, lower in the middle. */
export function waterline(x: number): number {
  const dx = (x + 0.5 - BW / 2) / RX
  return ABOVE + FRONT * Math.sqrt(Math.max(0, 1 - dx * dx))
}

/**
 * The brain, as in hand-made pixel art: not a ball but two lumpy halves with the temporal lobes
 * bulging low at the sides, an outline that ripples where the folds meet it; furrows grown as
 * wavy lines from the edge and from the middle, two pixels thick, with finer one-pixel ones in
 * between; each fold shaded as a pillow (lit on its upper-left edge, dark on its lower-right).
 * Below the waterline she fades into the green.
 */
export function paintBrain(): BrainArt {
  const p = new Painter(BW, BH)
  const cx = BW / 2
  const e = (x: number, y: number, ex: number, ey: number, rx: number, ry: number, tilt = 0) => {
    const dx = x - ex
    const dy = y - ey
    const u = dx * Math.cos(tilt) + dy * Math.sin(tilt)
    const v = -dx * Math.sin(tilt) + dy * Math.cos(tilt)
    return (u / rx) ** 2 + (v / ry) ** 2
  }
  const shape = (x: number, y: number) => {
    const px = x + 0.5
    const py = y + 0.5
    // A ripple on the outline: little bulges where the folds reach the edge.
    const bump = 1 + 0.09 * (noise(px / 5, py / 5, 71) - 0.5) + 0.05 * Math.sin(Math.atan2(py - 60, px - cx) * 14)
    const halves = Math.min(e(px, py, cx - 30, 58, 38, 55, 0.12), e(px, py, cx + 31, 57, 37, 56, -0.1))
    const lobes = Math.min(e(px, py, cx - 42, 88, 24, 22, 0.3), e(px, py, cx + 43, 87, 23, 22, -0.3))
    return Math.min(halves, lobes) <= bump
  }
  const ins = new Uint8Array(BW * BH)
  for (let y = 0; y < BH; y++) for (let x = 0; x < BW; x++) if (shape(x, y) && y < waterline(x) + 16) ins[y * BW + x] = 1
  const isIn = (x: number, y: number) => x >= 0 && y >= 0 && x < BW && y < BH && ins[y * BW + x] === 1

  // Furrows: wavy lines that keep their distance and join others when they meet.
  const furrow = new Uint8Array(BW * BH)
  const fine = new Uint8Array(BW * BH)
  const rnd = rand(29)
  let recent = new Set<number>()
  const free = (x: number, y: number, gap: number) => {
    if (!isIn(x, y) || Math.abs(x + 0.5 - cx) < 3) return false
    for (let dy = -gap; dy <= gap; dy++)
      for (let dx = -gap; dx <= gap; dx++) {
        if (dx * dx + dy * dy > gap * gap + 1) continue
        const k = (y + dy) * BW + x + dx
        if (k >= 0 && k < BW * BH && furrow[k] && !recent.has(k)) return false
      }
    return true
  }
  const grow = (x0: number, y0: number, angle: number, budget: number, gap: number, into: Uint8Array) => {
    let x = x0
    let y = y0
    const phase = rnd() * 6
    const wave = 0.35 + rnd() * 0.35
    let drift = (rnd() - 0.5) * 0.06
    recent = new Set<number>()
    const trail: number[] = []
    for (let step = 0; step < budget; step++) {
      drift += (rnd() - 0.5) * 0.02
      // Small waves along the line, and a slow turn.
      const th = angle + wave * Math.sin(phase + step * 0.55) + drift * step
      const nx = x + Math.cos(th)
      const ny = y + Math.sin(th)
      const px = Math.round(nx)
      const py = Math.round(ny)
      const k = py * BW + px
      if (k !== Math.round(y) * BW + Math.round(x)) {
        if (!free(px, py, gap)) {
          // Run on straight into the line that blocks it, so furrows meet.
          const path: number[] = []
          let jx = x
          let jy = y
          for (let n = 0; n <= gap + 1; n++) {
            jx += Math.cos(th)
            jy += Math.sin(th)
            const q = Math.round(jy) * BW + Math.round(jx)
            if (!ins[q]) break
            if (furrow[q] && !recent.has(q)) {
              for (const m of path) into[m] = 1
              break
            }
            path.push(q)
          }
          break
        }
        into[k] = 1
        trail.push(k)
        recent.add(k)
        if (into === furrow && trail.length > 10) recent.delete(trail[trail.length - 11])
      }
      x = nx
      y = ny
    }
  }
  // From the outline inwards, every few pixels, roughly across the edge.
  for (let a = 0; a < Math.PI * 2; a += 0.11 + rnd() * 0.05) {
    let x = cx + Math.cos(a) * 80
    let y = 60 + Math.sin(a) * 70
    for (let n = 0; n < 200 && !isIn(Math.round(x), Math.round(y)); n++) {
      x += (cx - x) * 0.02
      y += (60 - y) * 0.02
    }
    grow(x, y, Math.atan2(60 - y, cx - x) + (rnd() - 0.5) * 0.9, 18 + Math.floor(rnd() * 30), 5, furrow)
  }
  // From the middle furrow outwards.
  for (let y = 6; y < BH; y += 9 + Math.floor(rnd() * 5)) {
    for (const side of [-1, 1]) grow(cx + side * 3, y, (side < 0 ? Math.PI : 0) + (rnd() - 0.5) * 0.8, 20 + Math.floor(rnd() * 25), 5, furrow)
  }
  // Then wherever there is room.
  for (let k = 0; k < 3000; k++) {
    const x = 2 + Math.floor(rnd() * (BW - 4))
    const y = 2 + Math.floor(rnd() * (BH - 4))
    recent = new Set<number>()
    if (free(x, y, 6)) grow(x, y, rnd() * Math.PI * 2, 14 + Math.floor(rnd() * 26), 5, furrow)
  }
  // Two pixels thick.
  const thin = furrow.slice()
  for (let y = 1; y < BH; y++) for (let x = 1; x < BW; x++) if (ins[y * BW + x] && (thin[y * BW + x - 1] || thin[(y - 1) * BW + x])) furrow[y * BW + x] = 1
  // Finer, shallower furrows inside the wider folds: short dents, one pixel.
  for (let k = 0; k < 900; k++) {
    const x = 2 + Math.floor(rnd() * (BW - 4))
    const y = 2 + Math.floor(rnd() * (BH - 4))
    recent = new Set<number>()
    if (free(x, y, 3)) {
      const th = rnd() * Math.PI * 2
      for (let s = 0; s < 4 + Math.floor(rnd() * 4); s++) {
        const q = Math.round(y + Math.sin(th) * s + Math.sin(s) * 0.6) * BW + Math.round(x + Math.cos(th) * s)
        if (ins[q] && !furrow[q]) fine[q] = 1
      }
    }
  }

  // How far each pixel is from a furrow, and which way: a fold is lit on the side away from
  // the furrow above-left of it, shaded where a furrow lies below-right.
  const near = new Int32Array(BW * BH).fill(-1)
  const dist = new Float32Array(BW * BH).fill(99)
  const queue: number[] = []
  for (let k = 0; k < BW * BH; k++)
    if (furrow[k]) {
      near[k] = k
      dist[k] = 0
      queue.push(k)
    }
  for (let qi = 0; qi < queue.length; qi++) {
    const k = queue[qi]
    const x = k % BW
    const y = Math.floor(k / BW)
    for (const [dx, dy] of [
      [1, 0],
      [-1, 0],
      [0, 1],
      [0, -1],
    ]) {
      const nx = x + dx
      const ny = y + dy
      if (!isIn(nx, ny)) continue
      const m = ny * BW + nx
      if (near[m] >= 0) continue
      near[m] = near[k]
      const f = near[k]
      dist[m] = Math.hypot(nx - (f % BW), ny - Math.floor(f / BW))
      queue.push(m)
    }
  }

  const furrows = new Set<number>()
  for (let y = 0; y < BH; y++) {
    for (let x = 0; x < BW; x++) {
      const k = y * BW + x
      if (!ins[k]) continue
      const dx = (x + 0.5 - cx) / RX
      const dy = (y + 0.5 - 56) / 62
      // The whole is lit from the upper left, darker low down: in whole tones.
      const ball = Math.max(-1, Math.min(1, -0.5 * dx - 0.75 * dy + 0.3))
      let tone = 3 + Math.round(ball * 1.2)
      if (Math.abs(x + 0.5 - cx) < 1.5 && y < 100) tone = 1
      else if (furrow[k]) {
        tone = 1
        if (thin[k] && y < waterline(x)) furrows.add(k)
      } else {
        const f = near[k]
        const vx = x - (f % BW)
        const vy = y - Math.floor(f / BW)
        const d = dist[k]
        // Below-right of a furrow: the fold's lit edge. Above-left of one: its shaded edge.
        if (d <= 2.2 && vx + vy > 0) tone += 1
        else if (d <= 1.5 && vx + vy < 0) tone -= 1
        // The fold's crown, a little brighter.
        if (d >= 3.5 && vx + vy > 0) tone += 1
        if (fine[k]) tone -= 1
        tone = Math.max(2, Math.min(5, tone))
      }
      let c = CORAL[tone]
      if (tone === 5 && ball > 0.5 && dist[k] >= 3 && noise(x / 3, y / 3, 41) > 0.7) c = CORAL[6]
      // The green light from below, and under the liquid: fading into it.
      const w = waterline(x) - (y + 0.5)
      if (w < 0) {
        const k2 = Math.min(1, -w / 16)
        p.set(x, y, mix(mix(c, GREEN, 0.6), DEEP, 0.3 + k2 * 0.4), 0.55 * (1 - k2))
        continue
      }
      if (w < 1.5) c = mix(c, '#d9fff0', 0.55)
      else if (w < 8) c = mix(c, GREEN, 0.4)
      else if (w < 15) c = mix(c, GREEN, 0.18)
      p.set(x, y, c)
    }
  }
  // The outline, only above the liquid.
  const solid = new Painter(BW, BH)
  solid.data.set(p.data)
  solid.outline(CORAL[0], 250)
  for (let y = 0; y < BH; y++)
    for (let x = 0; x < BW; x++) {
      if (y + 0.5 > waterline(x) - 1) continue
      const i = (y * BW + x) * 4
      if (p.data[i + 3] === 0 && solid.data[i + 3] > 0) p.set(x, y, CORAL[0])
    }
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
      // Slow rings on the liquid, spreading from her; their back halves pass behind her.
      const rings = (front: boolean) => {
        for (let k = 0; k < 3; k++) {
          const ph = (t * 0.22 + k / 3) % 1
          const rx = RX + 4 + ph * (WELL - RX - 6)
          for (let a = front ? 0 : Math.PI; a < (front ? Math.PI : Math.PI * 2); a += 1.6 / rx) dot(ctx, x + Math.cos(a) * rx, y + LIQUID * C + Math.sin(a) * rx * S, '#e6fff4', 0.45 * (1 - ph))
        }
      }
      rings(false)
      ctx.drawImage(brain, bx, by)
      rings(true)
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
