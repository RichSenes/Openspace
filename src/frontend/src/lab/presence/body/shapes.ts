/**
 * The points Shiori is made of, for each skin. One being, several bodies: the shader moves
 * the points; this file only says where they start and what they are.
 *
 * Every point has three attributes:
 * - `pos`: a position in the body's own space (about -1..1);
 * - `data`: kind, seed, and two numbers whose meaning depends on the kind;
 * - `extra`: two more numbers (used by eye points for their "pleased" shape).
 */
/** Swarm and medusa are point bodies; the portrait is a drawn face (see ../portrait). */
export type SkinId = 'swarm' | 'medusa' | 'portrait'

export const SKIN_INDEX: Record<SkinId, number> = { swarm: 0, medusa: 1, portrait: 2 }

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

/** The portrait's height in body space; its width follows the drawing's proportions. */
export const PORTRAIT_UNITS = 2.2

/**
 * The portrait's points: one per art pixel of a `w`×`h` drawing, on a surface curved like a
 * head over shoulders. `data.zw` is where each point reads its light in the drawing.
 */
export function portraitPoints(w: number, h: number): PointSet {
  const b = new Builder()
  const r = rng(51)
  const H = PORTRAIT_UNITS
  const W = (H * w) / h
  for (let j = 0; j < h; j++)
    for (let i = 0; i < w; i++) {
      const u = (i + 0.5) / w
      const v = (j + 0.5) / h
      const x = (u - 0.5) * W
      const y = (0.5 - v) * H
      const head = Math.max(0, 1 - (x / 0.5) ** 2 - ((y - 0.45) / 0.7) ** 2)
      const torso = Math.max(0, 1 - (x / 1.0) ** 2) * (y < -0.1 ? 1 : 0)
      const z = 0.32 * Math.sqrt(head) + 0.16 * Math.sqrt(torso)
      b.add([x, y, z], [KIND.body, r(), u, v])
    }
  return b.build()
}

/** The points of a body. */
export function buildSkin(id: SkinId): PointSet {
  if (id === 'medusa') return medusa()
  if (id === 'portrait') return portraitPoints(128, 176)
  return swarm()
}
