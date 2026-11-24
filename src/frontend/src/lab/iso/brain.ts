import { SQUASH } from './iso'
import { dot, glow } from './props'
import { mix, noise, Painter, rand } from './px'

/**
 * Shiori in the lab: a very large brain sunk to half its height in a well of green light, seen
 * from the front, under a round glass dome that rises from the floor. Painted by tracing a ray
 * per pixel through simple shapes, with the room's own camera, so the dome is truly round and
 * the floor truly cuts the brain; then the light is quantised to a few tones, as pixel art is.
 */

/** The camera: its elevation's sine and cosine. A point (X right, Y away, Z up) lands at (X, -Y·s - Z·c). */
const S = SQUASH
const C = Math.sqrt(1 - S * S)

/** Sizes in pixels. */
const HEMI_RX = 38
const HEMI_RY = 70
const HEMI_RZ = 46
const HEMI_X = 27
const WELL = 80
const LIP = 6
export const DOME_R = 92

const W = 2 * DOME_R + 16
const TOP = DOME_R + 8
const H = TOP + Math.ceil((DOME_R + LIP) * S) + 10

const PINK = ['#26112f', '#3e1a4c', '#5e2767', '#843a80', '#ad5597', '#d27fb0', '#efb0cc', '#fbd8e6']
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

/** Where a ray first enters an ellipsoid, if it does. */
function hitEllipsoid(o: V, c: V, r: V): number | null {
  const ox = (o[0] - c[0]) / r[0]
  const oy = (o[1] - c[1]) / r[1]
  const oz = (o[2] - c[2]) / r[2]
  const dx = DIR[0] / r[0]
  const dy = DIR[1] / r[1]
  const dz = DIR[2] / r[2]
  const A = dx * dx + dy * dy + dz * dz
  const B = 2 * (ox * dx + oy * dy + oz * dz)
  const K = ox * ox + oy * oy + oz * oz - 1
  const disc = B * B - 4 * A * K
  if (disc < 0) return null
  return (-B - Math.sqrt(disc)) / (2 * A)
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

/** How high the gyri are at a point of the surface: 0 in a furrow, 1 on top of a fold. */
function gyrus(p: V): number {
  const w = 0.8 * noise(p[0] / 18, p[1] / 18 + p[2] / 24, 3)
  const f = noise(p[0] / 13 + w, p[1] / 15 + p[2] / 13, 11) * 0.65 + noise(p[0] / 13 + 5, p[2] / 12 - p[1] / 30 + w, 17) * 0.35
  // A slow slope under the noise keeps the folds running across, side to side, never pooling.
  const s = (((f * 3.6 + p[1] / 15 + p[2] / 22) % 1) + 1) % 1
  return Math.sin(Math.PI * s)
}

interface Sample {
  kind: 'brain' | 'lip' | 'well' | 'wall' | 'rim'
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
  let best: Sample | null = null
  for (const side of [-1, 1]) {
    const c: V = [side * HEMI_X, 0, 0]
    const t = hitEllipsoid(o, c, [HEMI_RX, HEMI_RY, HEMI_RZ])
    if (t === null || t > tf) continue
    if (best && t >= best.depth) continue
    const p = along(o, t)
    const n = norm([(p[0] - c[0]) / HEMI_RX ** 2, (p[1] - c[1]) / HEMI_RY ** 2, (p[2] - c[2]) / HEMI_RZ ** 2])
    best = { kind: 'brain', p, n, g: gyrus(p), depth: t }
  }
  if (best) return best
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

const KEY = norm([-0.5, -0.45, 0.75])
const VIEW: V = [0, -C, S]

function paintWell(): {
  canvas: HTMLCanvasElement
  furrows: [number, number][]
} {
  const p = new Painter(W, H)
  const samples: (Sample | null)[] = []
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) samples.push(trace(x + 0.5 - W / 2, y + 0.5 - TOP))
  const furrows: [number, number][] = []
  const gAt = (x: number, y: number, fallback: number) => {
    const s = x >= 0 && y >= 0 && x < W && y < H ? samples[y * W + x] : null
    return s && s.kind === 'brain' ? s.g : fallback
  }
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const s = samples[y * W + x]
      if (!s) continue
      if (s.kind === 'brain') {
        // The folds are shapes: the surface bends with them, so they catch the light and cast dark.
        const gx = gAt(x + 1, y, s.g) - gAt(x - 1, y, s.g)
        const gy = gAt(x, y + 1, s.g) - gAt(x, y - 1, s.g)
        const n = norm([s.n[0] - 1.3 * gx, s.n[1] + 1.3 * gy * S, s.n[2] + 1.3 * gy * C])
        const diffuse = Math.max(0, dotv(n, KEY))
        const rim = (1 - Math.abs(dotv(n, VIEW))) ** 2
        let level = diffuse * 0.8 + 0.12 + rim * 0.25
        if (s.g < 0.22) level -= 0.32 * (1 - s.g / 0.22)
        // The furrow between the halves.
        if (Math.abs(s.p[0]) < 3) level -= 0.25
        let c = PINK[Math.max(0, Math.min(PINK.length - 1, Math.floor(level * PINK.length)))]
        // The green light from the well, on what is near the floor.
        const low = Math.max(0, 1 - s.p[2] / 22) * (0.55 + 0.45 * Math.max(0, -n[2] + 0.3))
        if (low > 0.55) c = mix(c, GREEN, 0.55)
        else if (low > 0.28) c = mix(c, GREEN, 0.28)
        p.set(x, y, c)
        if (s.g < 0.18 && Math.abs(s.p[0]) > 3) furrows.push([x, y])
      } else if (s.kind === 'wall') {
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

export function makeBrain(): Brain {
  const { canvas: well, furrows } = paintWell()
  const glass = paintGlass()
  const r = rand(17)
  const particles: Particle[] = Array.from({ length: 90 }, (_, k) => ({
    r: 58 + r() * 26,
    tilt: 0.2 + r() * 1.2,
    yaw: r() * Math.PI * 2,
    speed: (4 + r() * 4) * (k % 5 === 0 ? -1 : 1),
    ph: r() * Math.PI * 2,
    color: ['#9ff3ff', '#ffffff', '#d8a6ff', '#7ff0ff', '#b8ffe0'][k % 5],
    big: r() > 0.8,
  }))

  return {
    draw(ctx, x, y, t) {
      const ox = Math.round(x - W / 2)
      const oy = Math.round(y - TOP)
      const placed = particles.map((p) => place(p, t))
      for (const p of placed) if (p.far) trail(ctx, x, y, p)
      ctx.drawImage(well, ox, oy)
      // The light of the well, breathing.
      glow(ctx, x, y + 4, 70, GREEN, 0.3 + 0.08 * Math.sin(t * 1.6))
      // Waves of thought running along the furrows.
      for (let w = 0; w < 3; w++) {
        const ph = (t * 0.45 + w / 3) % 1
        const turn = Math.floor(t * 0.45 + w / 3)
        const cx = W / 2 + Math.cos(w * 2.1 + turn) * 36
        const cy = TOP - 30 + Math.sin(w * 1.7 + turn) * 14
        const radius = ph * 90
        for (const [fx, fy] of furrows) if (Math.abs(Math.hypot(fx - cx, fy - cy) - radius) < 1.3) dot(ctx, ox + fx, oy + fy, '#9ff3ff', 0.9 * (1 - ph))
      }
      for (const p of placed) if (!p.far) trail(ctx, x, y, p)
      glow(ctx, x, y - 50, 90, '#c26bff', 0.12 + 0.04 * Math.sin(t * 1.7))
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
  for (let k = 7; k >= 0; k--) {
    const q = orbit(p, th - k * step)
    if (q[2] < 0) continue
    const sx = cx + q[0]
    const sy = cy - q[1] * S - q[2] * C
    dot(ctx, sx, sy, p.color, k === 0 ? 1 : 0.7 * (1 - k / 9), k === 0 && p.big ? 2 : 1)
    if (k === 0) glow(ctx, sx, sy, 4, p.color, 0.5)
  }
}
