import type { SkinId } from './shapes'

type V3 = [number, number, number]

/** A 3×3 rotation, column-major as GLSL reads it: yaw around y, then pitch around x. */
export function rotation(yaw: number, pitch: number): Float32Array {
  const cy = Math.cos(yaw)
  const sy = Math.sin(yaw)
  const cp = Math.cos(pitch)
  const sp = Math.sin(pitch)
  // R = Rx(pitch) * Ry(yaw)
  const m = [
    [cy, 0, sy],
    [sy * sp, cp, -cy * sp],
    [-sy * cp, sp, cy * cp],
  ]
  // Column-major: columns are m[*][j].
  return new Float32Array([m[0][0], m[1][0], m[2][0], m[0][1], m[1][1], m[2][1], m[0][2], m[1][2], m[2][2]])
}

export function apply(m: Float32Array, v: V3): V3 {
  return [m[0] * v[0] + m[3] * v[1] + m[6] * v[2], m[1] * v[0] + m[4] * v[1] + m[7] * v[2], m[2] * v[0] + m[5] * v[1] + m[8] * v[2]]
}

export const IDENTITY = rotation(0, 0)

/** How each body sits: its size, where the eyes go, its colours. */
export interface SkinRig {
  label: string
  line: string
  bodyScale: number
  lift: number
  eyeCenter: V3
  /** Half width, half height, half distance between the eyes. */
  eyeSize: V3
  cool: V3
  hot: V3
  eye: V3
  /** Whether the eyes turn with the body (a head) or always face you (a creature). */
  eyesTurn: boolean
  /** Parts named by the HUD, in body space. */
  parts: { name: string; at: V3 }[]
}

export const RIGS: Record<SkinId, SkinRig> = {
  swarm: {
    label: 'Swarm',
    line: 'A membrane of light; thoughts crawl on it',
    bodyScale: 0.8,
    lift: 0,
    eyeCenter: [0, 0.04, 0.86],
    eyeSize: [0.1, 0.19, 0.25],
    cool: [0.42, 0.46, 1.0],
    hot: [1.0, 0.42, 0.74],
    eye: [0.86, 0.95, 1.0],
    eyesTurn: false,
    parts: [
      { name: 'MEMBRANE', at: [0.62, 0.55, 0.45] },
      { name: 'NUCLEUS', at: [-0.7, -0.35, 0.5] },
      { name: 'TRACE', at: [0.2, -0.8, 0.5] },
    ],
  },
  medusa: {
    label: 'Medusa',
    line: 'A creature of the deep, lit from inside',
    bodyScale: 0.78,
    lift: 0.3,
    eyeCenter: [0, -0.02, 0.8],
    eyeSize: [0.08, 0.13, 0.21],
    cool: [0.3, 0.62, 1.0],
    hot: [1.0, 0.32, 0.78],
    eye: [0.8, 1.0, 1.0],
    eyesTurn: false,
    parts: [
      { name: 'BELL', at: [0.55, 0.3, 0.4] },
      { name: 'TENTACLE', at: [-0.5, -0.9, 0.3] },
      { name: 'OCELLUS', at: [-0.75, -0.05, 0.3] },
    ],
  },
  scanline: {
    label: 'Scanline',
    line: 'A head drawn by a scanner, line by line',
    bodyScale: 0.9,
    lift: 0.05,
    eyeCenter: [0, 0.4, 0.4],
    eyeSize: [0.055, 0.032, 0.13],
    cool: [0.82, 0.84, 1.0],
    hot: [1.0, 0.33, 0.22],
    eye: [1.0, 1.0, 1.0],
    eyesTurn: true,
    parts: [
      { name: 'CORTEX', at: [0.22, 0.75, 0.2] },
      { name: 'VOICE', at: [0.0, 0.12, 0.3] },
      { name: 'SPINE', at: [-0.12, -0.3, 0.1] },
    ],
  },
}

/** Body rotation for a skin: creatures drift around, a head follows the gaze. */
export function bodyRotation(skin: SkinId, phase: number, time: number, gaze: [number, number], lean: number): { rot: Float32Array; eyeRot: Float32Array } {
  if (skin === 'scanline') {
    const yaw = 0.45 * Math.sin(time * 0.23) + gaze[0] * 0.45
    const pitch = -gaze[1] * 0.18 + lean * 0.08
    const rot = rotation(yaw, pitch)
    return { rot, eyeRot: rot }
  }
  if (skin === 'medusa') return { rot: rotation(phase * 0.08, 0.18 + 0.05 * Math.sin(time * 0.3)), eyeRot: IDENTITY }
  return { rot: rotation(phase * 0.12, 0.25 * Math.sin(phase * 0.07) + lean * 0.12), eyeRot: IDENTITY }
}

/** Where a body point lands on the canvas, in CSS pixels, as the point shader projects it. */
export function project(
  p: V3,
  rot: Float32Array,
  rig: Pick<SkinRig, 'bodyScale' | 'lift'>,
  view: { w: number; h: number; scale: number; offset: [number, number] },
): { x: number; y: number; depth: number } {
  const v = apply(rot, [p[0] * rig.bodyScale, p[1] * rig.bodyScale, p[2] * rig.bodyScale])
  v[1] += rig.lift
  const persp = 3 / (3 - v[2])
  const cx = (v[0] * persp * view.scale) / (view.w / view.h) + view.offset[0]
  const cy = v[1] * persp * view.scale + view.offset[1]
  return { x: ((cx + 1) / 2) * view.w, y: ((1 - cy) / 2) * view.h, depth: (v[2] + 1) / 2 }
}

/** The colours each mode lends to Shiori's heat. */
export const MODE_HEAT: Record<string, V3> = {
  brainstorm: [0.45, 0.72, 1.0],
  decide: [1.0, 0.72, 0.3],
  organise: [0.55, 1.0, 0.55],
}

export function mix3(a: V3, b: V3, t: number): V3 {
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]
}

/** The dystopian world's palette for pixel mode: night, smog, neon, fire. Black stays black. */
export const DYSTOPIA_PALETTE: V3[] = [
  [0, 0, 0],
  [0.1, 0.07, 0.22],
  [0.2, 0.19, 0.5],
  [0.31, 0.49, 1.0],
  [0.61, 0.42, 1.0],
  [1.0, 0.3, 0.6],
  [1.0, 0.62, 0.34],
  [0.95, 0.96, 1.0],
]
