/**
 * The points Shiori is made of, for each skin. One being, several bodies: the shader moves
 * the points; this file only says where they start and what they are.
 *
 * Every point has three attributes:
 * - `pos`: a position in the body's own space (about -1..1);
 * - `data`: kind, seed, and two numbers whose meaning depends on the kind;
 * - `extra`: two more numbers (used by eye points for their "pleased" shape).
 */
export type SkinId = 'swarm' | 'medusa' | 'scanline'

export const SKIN_INDEX: Record<SkinId, number> = { swarm: 0, medusa: 1, scanline: 2 }

/** What a point is: the shader treats each kind differently. */
export const KIND = {
  body: 0,
  /** A tentacle point (medusa) or a trail point (swarm worms). */
  strand: 1,
  leftEye: 2,
  rightEye: 3,
} as const

export interface PointSet {
  count: number
  pos: Float32Array
  data: Float32Array
  extra: Float32Array
}

/** A small deterministic generator, so a skin looks the same on every visit and in tests. */
export function rng(seed: number): () => number {
  let s = seed >>> 0 || 1
  return () => {
    s ^= s << 13
    s ^= s >>> 17
    s ^= s << 5
    return (s >>> 0) / 4294967296
  }
}

class Builder {
  private pos: number[] = []
  private data: number[] = []
  private extra: number[] = []
  add(p: [number, number, number], d: [number, number, number, number], e: [number, number] = [0, 0]) {
    this.pos.push(...p)
    this.data.push(...d)
    this.extra.push(...e)
  }
  build(): PointSet {
    return {
      count: this.pos.length / 3,
      pos: new Float32Array(this.pos),
      data: new Float32Array(this.data),
      extra: new Float32Array(this.extra),
    }
  }
}

/** Points spread evenly on a unit sphere (golden spiral). */
export function fibonacciSphere(n: number): [number, number, number][] {
  const out: [number, number, number][] = []
  const golden = Math.PI * (3 - Math.sqrt(5))
  for (let i = 0; i < n; i++) {
    const y = 1 - ((i + 0.5) / n) * 2
    const r = Math.sqrt(1 - y * y)
    const a = golden * i
    out.push([Math.cos(a) * r, y, Math.sin(a) * r])
  }
  return out
}

/** A random point on the unit sphere. */
export function onSphere(r: () => number): [number, number, number] {
  const y = r() * 2 - 1
  const a = r() * Math.PI * 2
  const k = Math.sqrt(1 - y * y)
  return [Math.cos(a) * k, y, Math.sin(a) * k]
}

/**
 * The eyes: a disc of points per eye. Each point also knows where it goes when Shiori is
 * pleased (an upturned arc, like a smile with the eyes). Eye points start somewhere on the body
 * and gather into the eyes when Shiori looks at you.
 */
function addEyes(b: Builder, perEye: number, origin: (r: () => number) => [number, number, number], seed: number) {
  const r = rng(seed)
  for (const kind of [KIND.leftEye, KIND.rightEye]) {
    for (let i = 0; i < perEye; i++) {
      // An even disc (sunflower), in -1..1.
      const k = (i + 0.5) / perEye
      const rad = Math.sqrt(k)
      const a = i * 2.39996
      const dx = Math.cos(a) * rad
      const dy = Math.sin(a) * rad
      // The arc: the same points laid on a thick upturned "∩", by angle.
      const t = (Math.atan2(dy, dx) + Math.PI) / (2 * Math.PI)
      const ang = Math.PI * (0.08 + 0.84 * t)
      const band = 0.62 + 0.38 * rad
      const ax = Math.cos(ang) * band
      const ay = Math.sin(ang) * band * 0.75 - 0.25
      b.add(origin(r), [kind, r(), dx, dy], [ax, ay])
    }
  }
}

/** Swarm: a membrane of points on a sphere that the shader deforms with noise. */
export function swarm(n = 2600, eyes = 320): PointSet {
  const b = new Builder()
  const r = rng(11)
  for (const p of fibonacciSphere(n)) b.add(p, [KIND.body, r(), 0, 0])
  addEyes(b, eyes, (rr) => onSphere(rr), 12)
  return b.build()
}

/**
 * Medusa: a bell (a dome of points, its `u` from the top to the rim) and tentacles hanging from
 * the rim (their `s` from the rim to the tip). The shader pulses the bell and sways the strands.
 */
export function medusa(bell = 1500, tentacles = 30, perTentacle = 80, eyes = 110): PointSet {
  const b = new Builder()
  const r = rng(21)
  for (let i = 0; i < bell; i++) {
    const u = Math.sqrt((i + 0.5) / bell)
    const phi = i * 2.39996
    const rad = Math.sin(u * 1.45) * 0.95
    const y = 0.42 - (1 - Math.cos(u * 1.45)) * 0.62
    b.add([Math.cos(phi) * rad, y, Math.sin(phi) * rad], [KIND.body, r(), u, phi])
  }
  // Inner frills under the bell: a second, smaller skirt.
  for (let i = 0; i < Math.round(bell * 0.25); i++) {
    const u = 0.5 + r() * 0.5
    const phi = r() * Math.PI * 2
    const rad = Math.sin(u * 1.45) * 0.55
    const y = 0.15 - (1 - Math.cos(u * 1.45)) * 0.5
    b.add([Math.cos(phi) * rad, y, Math.sin(phi) * rad], [KIND.body, r(), u, phi])
  }
  const rimRad = Math.sin(1.45) * 0.95
  const rimY = 0.42 - (1 - Math.cos(1.45)) * 0.62
  for (let t = 0; t < tentacles; t++) {
    const inner = t >= tentacles - 6
    const phi = inner ? ((t - (tentacles - 6)) / 6) * Math.PI * 2 + 0.4 : (t / (tentacles - 6)) * Math.PI * 2 + r() * 0.15
    const rad = inner ? 0.2 : rimRad * (0.92 + r() * 0.08)
    const seed = r()
    for (let i = 0; i < perTentacle; i++) {
      const s = i / (perTentacle - 1)
      b.add([Math.cos(phi) * rad, inner ? rimY + 0.1 : rimY, Math.sin(phi) * rad], [KIND.strand, seed, s, inner ? 1 : 0])
    }
  }
  addEyes(b, eyes, (rr) => {
    const u = rr()
    const phi = rr() * Math.PI * 2
    return [Math.cos(phi) * Math.sin(u * 1.45) * 0.95, 0.42 - (1 - Math.cos(u * 1.45)) * 0.62, Math.sin(phi) * Math.sin(u * 1.45) * 0.95]
  }, 22)
  return b.build()
}

const bump = (y: number, c: number, w: number) => Math.exp(-(((y - c) / w) ** 2))
const smooth = (a: number, b: number, x: number) => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)))
  return t * t * (3 - 2 * t)
}

/**
 * The bust at height y, as a cross-section: half width, and depth toward the front and the
 * back. A woman's head drawn by a scanner: skull, soft brow, small nose, full lips, a narrow
 * chin, high cheekbones, a slender neck and shoulders that slope down from it. Null above it.
 */
export function bustSection(y: number): { rx: number; front: number; back: number } | null {
  if (y > 0.86) return null
  let rx = 0
  let front = 0
  let back = 0
  // Head: skull above, a jaw that narrows softly to a small chin below.
  if (y >= 0.05) {
    const skull = Math.sqrt(Math.max(0, 1 - ((y - 0.47) / 0.39) ** 2))
    rx = y >= 0.42 ? 0.29 * skull : 0.1 + 0.18 * smooth(0.05, 0.42, y) ** 0.6
    rx += 0.014 * bump(y, 0.36, 0.05)
    back = y >= 0.3 ? Math.max(0.13, 0.37 * Math.sqrt(Math.max(0, 1 - ((y - 0.5) / 0.4) ** 2))) : 0.13
    const forehead = 0.34 * Math.sqrt(Math.max(0, 1 - ((y - 0.5) / 0.37) ** 2))
    front = y > 0.56 ? forehead : 0.31
    front += 0.012 * bump(y, 0.53, 0.025) - 0.025 * bump(y, 0.46, 0.035)
    front += 0.028 * bump(y, 0.215, 0.016) - 0.018 * bump(y, 0.197, 0.006) + 0.026 * bump(y, 0.178, 0.016)
    front += 0.015 * bump(y, 0.1, 0.03)
    front = front * smooth(0.0, 0.11, y) + 0.11 * (1 - smooth(0.0, 0.11, y))
  }
  // A slender neck, the shoulders sloping out, then the chest.
  if (y < 0.15) {
    const t = Math.min(1, Math.max(0, (-0.13 - y) / 0.38))
    const slope = 1 - (1 - t) ** 2.4
    const nrx = y > -0.13 ? 0.11 : 0.11 + 0.52 * slope - (y < -0.51 ? 0.03 * ((-0.51 - y) / 0.5) : 0)
    const nfront = 0.11 + 0.09 * slope + 0.03 * bump(y, -0.74, 0.12)
    const nback = 0.12 + 0.06 * slope
    rx = Math.max(rx, nrx)
    front = Math.max(front, nfront)
    back = Math.max(back, nback)
  }
  return { rx, front, back }
}

/** How far the nose stands out at height y (a small one, its tip at 0.31). */
export function noseAt(y: number): number {
  if (y > 0.45 || y < 0.28) return 0
  return y >= 0.31 ? 0.06 * ((0.45 - y) / 0.14) : 0.06 * ((y - 0.28) / 0.03)
}

/** Where the face shows through the hair: the front, within this sine of the angle. */
const FACE_OPEN = 0.5
/** The fringe: hair covers the forehead down to here. */
const FRINGE = 0.535

/**
 * Long straight hair with a blunt fringe, as a shell around the head: a cap over the skull,
 * curtains down the sides and the back, ending in uneven tips below the shoulders. Null where
 * there is no hair at that height and angle.
 */
export function hairAt(y: number, phi: number, tip: number): { r: number; depth: number } | null {
  if (y > 0.91 || y < -0.62 + tip) return null
  const sn = Math.sin(phi)
  const head = bustSection(Math.min(0.86, Math.max(0.42, y)))!
  const skullTop = y > 0.84 ? Math.sqrt(Math.max(0, 1 - ((y - 0.84) / 0.07) ** 2)) : 1
  if (y >= FRINGE) {
    // The cap: all around, a little outside the skull.
    const k = y > 0.84 ? skullTop : 1
    return { r: (head.rx + 0.035) * k, depth: ((sn >= 0 ? head.front : head.back) + 0.04) * k }
  }
  if (sn > FACE_OPEN) return null
  if (y >= 0.1) return { r: head.rx + 0.045 + 0.02 * (1 - y), depth: sn >= 0 ? head.front * 0.85 : head.back + 0.05 }
  // Below the chin the hair falls straight: two curtains over the front of the shoulders, and
  // the back. The front of the chest stays clear.
  const fall = Math.min(1, (0.1 - y) / 0.6)
  const c = Math.abs(Math.cos(phi))
  if (sn > 0 && c < 0.6) return null
  const body = bustSection(y)
  const r = 0.27 + 0.07 * fall
  if (body && body.rx > r - 0.02 && sn > 0) {
    // Over a shoulder: lie on it.
    return { r, depth: body.front + 0.03 }
  }
  return { r, depth: sn >= 0 ? 0.16 + 0.05 * fall : 0.27 + 0.05 * fall }
}

/**
 * Scanline: the bust drawn as horizontal dashes, line by line, like a scan. `data.z` is the
 * line's index (0..1, bottom to top), `data.w` the angle around the body; `extra.x` is 1 for hair.
 */
export function scanline(lines = 82, spacing = 0.031, eyes = 90): PointSet {
  const b = new Builder()
  const r = rng(31)
  const top = 0.91
  const bottom = -0.98
  // Every column of hair ends at its own length.
  const tips = Array.from({ length: 64 }, () => r() * 0.16)
  for (let l = 0; l < lines; l++) {
    const y = bottom + ((top - bottom) * l) / (lines - 1)
    const sec = bustSection(y)
    const around = sec ? 2 * Math.PI * Math.sqrt((sec.rx * sec.rx + ((sec.front + sec.back) / 2) ** 2) / 2) : 1.4
    const n = Math.max(24, Math.round(Math.max(around, 1.9) / spacing))
    for (let i = 0; i < n; i++) {
      const phi = (i / n) * Math.PI * 2
      const c = Math.cos(phi)
      const sn = Math.sin(phi)
      const hair = hairAt(y, phi, tips[Math.floor((i / n) * tips.length)])
      if (hair) b.add([c * hair.r, y, sn * hair.depth], [KIND.body, r(), l / (lines - 1), phi], [1, 0])
      // Skin only where the hair does not cover it.
      if (!sec || (hair && y > 0.05)) continue
      const nose = noseAt(y) * Math.max(0, sn) ** 40
      const depth = sn >= 0 ? sec.front + nose : sec.back
      b.add([c * sec.rx, y, sn * depth], [KIND.body, r(), l / (lines - 1), phi])
    }
  }
  addEyes(b, eyes, (rr) => [rr() * 0.5 - 0.25, 0.2 + rr() * 0.5, 0.2], 32)
  return b.build()
}

export function buildSkin(id: SkinId): PointSet {
  if (id === 'medusa') return medusa()
  if (id === 'scanline') return scanline()
  return swarm()
}
