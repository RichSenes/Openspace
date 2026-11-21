import type { FaceState } from '../../shiori/faceMachine'

/**
 * How Shiori's body behaves in each state. Every skin reads the same numbers, so the
 * character stays the same whatever body she wears.
 */
export interface Pose {
  /** 0..1: how bright and how much the surface moves. */
  energy: number
  /** Multiplier of all motion. */
  speed: number
  /** 0..1: the eyes gathered (1) or dissolved back into the body (0). */
  eyes: number
  /** Eye opening: 1 normal, more is wide, less is narrow. */
  open: number
  /** 0..1: eyes turned into happy arcs. */
  pleased: number
  /** 0..1: eyes small, tilted and low. */
  unsure: number
  /** 0..1: leaning toward the person, slightly smaller and denser. */
  lean: number
  /** 0..1: how hot the colour runs (thoughts, warmth). */
  heat: number
}

export const POSES: Record<FaceState, Pose> = {
  idle: { energy: 0.35, speed: 0.55, eyes: 1, open: 1, pleased: 0, unsure: 0, lean: 0, heat: 0.15 },
  listening: { energy: 0.55, speed: 0.75, eyes: 1, open: 1.2, pleased: 0, unsure: 0, lean: 1, heat: 0.1 },
  thinking: { energy: 0.75, speed: 1.9, eyes: 0, open: 0.8, pleased: 0, unsure: 0, lean: 0, heat: 0.8 },
  speaking: { energy: 0.8, speed: 1.1, eyes: 1, open: 1, pleased: 0, unsure: 0, lean: 0.3, heat: 0.35 },
  pleased: { energy: 0.9, speed: 0.9, eyes: 1, open: 1, pleased: 1, unsure: 0, lean: 0.2, heat: 0.55 },
  unsure: { energy: 0.4, speed: 0.5, eyes: 1, open: 0.75, pleased: 0, unsure: 1, lean: 0, heat: 0.25 },
}

/** Moves every number of a pose toward a target; `k` per second, framerate independent. */
export function approach(from: Pose, to: Pose, dt: number, k = 5): Pose {
  const a = 1 - Math.exp(-k * dt)
  const out = { ...from }
  for (const key of Object.keys(to) as (keyof Pose)[]) out[key] = from[key] + (to[key] - from[key]) * a
  return out
}

/**
 * Blinks and glances: what keeps a face alive between states. Pure, driven by time and a
 * random source, so it can be tested and replayed.
 */
export interface Life {
  /** Seconds until the next blink. */
  nextBlink: number
  /** Seconds left in the current blink (0 = open). */
  blink: number
  /** A second blink right after this one. */
  double: boolean
  nextGlance: number
  gaze: [number, number]
  target: [number, number]
}

export const BLINK = 0.16

export function makeLife(): Life {
  return { nextBlink: 2, blink: 0, double: false, nextGlance: 1.2, gaze: [0, 0], target: [0, 0] }
}

/**
 * Advances blinks and glances by dt. `look` is where the state wants the eyes (for example
 * toward the chat when listening); glances wander around it.
 */
export function stepLife(l: Life, dt: number, rand: () => number, look: [number, number], wander: number): Life {
  let { nextBlink, blink, double, nextGlance } = l
  let target = l.target
  if (blink > 0) {
    blink = Math.max(0, blink - dt)
    if (blink === 0 && double) {
      double = false
      nextBlink = 0.12
    }
  } else {
    nextBlink -= dt
    if (nextBlink <= 0) {
      blink = BLINK
      double = rand() < 0.2
      nextBlink = 2.4 + rand() * 4
    }
  }
  nextGlance -= dt
  if (nextGlance <= 0) {
    target = [look[0] + (rand() * 2 - 1) * wander, look[1] + (rand() * 2 - 1) * wander * 0.6]
    nextGlance = 0.8 + rand() * 2.4
  }
  // Saccades: eyes jump fast toward the target, then rest.
  const a = 1 - Math.exp(-18 * dt)
  const gaze: [number, number] = [l.gaze[0] + (target[0] - l.gaze[0]) * a, l.gaze[1] + (target[1] - l.gaze[1]) * a]
  return { nextBlink, blink, double, nextGlance, gaze, target }
}

/** Eye opening from the blink: 1 open, near 0 shut, with a fast close and a softer open. */
export function blinkOpen(l: Life): number {
  if (l.blink <= 0) return 1
  const p = 1 - l.blink / BLINK
  return p < 0.4 ? 1 - p / 0.4 : 0.08 + ((p - 0.4) / 0.6) * 0.92
}

/** Where each state looks: toward the chat below when listening, up and aside when thinking. */
export const LOOKS: Record<FaceState, [number, number]> = {
  idle: [0, 0],
  listening: [0, 0.55],
  thinking: [-0.5, -0.7],
  speaking: [0, 0.25],
  pleased: [0, 0],
  unsure: [-0.35, 0.45],
}

/**
 * A voice envelope while Shiori speaks: syllables as bumps, words as small gaps. Stands in for
 * the real audio level until there is a voice.
 */
export function voiceLevel(t: number): number {
  const syll = Math.max(0, Math.sin(t * 13.7)) ** 0.6
  const words = 0.5 + 0.5 * Math.sin(t * 3.1 + Math.sin(t * 1.3) * 2)
  return Math.min(1, syll * (0.35 + 0.65 * words))
}
