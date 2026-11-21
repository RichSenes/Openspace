import type { FaceState } from '../../shiori/faceMachine'
import { blinkOpen, LOOKS, makeLife, stepLife, voiceLevel, type Life } from '../body/character'
import { NEUTRAL, type Expression } from './expression'

/**
 * How Shiori's face moves: one expression per state, reached through springs (so a change
 * overshoots a little and settles, as faces do), a blink when her thought changes, eyes that move
 * before the head follows, breathing, and a mouth that shapes words while she speaks.
 */
export const EXPRESSIONS: Record<FaceState, Partial<Expression>> = {
  idle: { lid: 0.94, cheek: 0.1, brow: 0, browTilt: 0, smile: 0.2, open: 0, wide: 0 },
  listening: { lid: 1.08, cheek: 0, brow: 0.45, browTilt: 0, smile: 0.12, open: 0.08, wide: 0 },
  thinking: { lid: 0.72, cheek: 0, brow: -0.25, browTilt: 0.55, smile: -0.15, open: 0, wide: -0.6 },
  speaking: { lid: 1, cheek: 0.15, brow: 0.2, browTilt: 0, smile: 0.25, open: 0, wide: 0 },
  pleased: { lid: 0.62, cheek: 1, brow: 0.35, browTilt: 0, smile: 1, open: 0.15, wide: 0.4 },
  unsure: { lid: 0.86, cheek: 0, brow: -0.1, browTilt: -0.85, smile: -0.45, open: 0, wide: -0.3 },
}

/** How far the head turns toward where the eyes look, and the state's own lean. */
const HEAD: Record<FaceState, number> = { idle: 0, listening: 0.1, thinking: -0.35, speaking: 0.05, pleased: 0.18, unsure: 0.25 }

type Key = keyof Expression

interface Spring {
  x: number
  v: number
}

export interface FaceMotion {
  springs: Record<Key, Spring>
  life: Life
  state: FaceState
  time: number
  /** Breathing: the portrait rises by this many art pixels (0 or 1). */
  breath: number
}

const KEYS = Object.keys(NEUTRAL) as Key[]

/** Stiffness per part: lids and mouth are quick, brows a little slower, the head slowest. */
const STIFF: Partial<Record<Key, number>> = { lid: 260, open: 320, wide: 220, smile: 120, cheek: 100, brow: 110, browTilt: 110, gazeX: 400, gazeY: 400, turn: 40, tilt: 40 }

export function makeFace(state: FaceState = 'idle'): FaceMotion {
  const target = { ...NEUTRAL, ...EXPRESSIONS[state] }
  const springs = Object.fromEntries(KEYS.map((k) => [k, { x: target[k], v: 0 }])) as Record<Key, Spring>
  return { springs, life: makeLife(), state, time: 0, breath: 0 }
}

/** The target expression of a state at a moment: speech adds the mouth's shapes. */
export function targetOf(state: FaceState, time: number, gaze: [number, number], calm: boolean): Expression {
  const t = { ...NEUTRAL, ...EXPRESSIONS[state] }
  t.gazeX = gaze[0]
  t.gazeY = gaze[1]
  // The head follows the eyes, part of the way.
  t.turn = gaze[0] * 0.45 + HEAD[state]
  if (state === 'speaking' && !calm) {
    const level = voiceLevel(time)
    t.open = level * 0.85
    // Vowels: the mouth widens and rounds as words go by.
    t.wide = Math.sin(time * 7.3) * 0.6 * level
  }
  return t
}

/**
 * Advances the face by dt seconds. Springs are a little under-damped, so every change lands
 * with a small overshoot and settles. A change of state starts with a blink.
 */
export function stepFace(f: FaceMotion, dt: number, state: FaceState, rand: () => number, calm: boolean): FaceMotion {
  let life = f.life
  if (state !== f.state && life.blink === 0) life = { ...life, blink: 0.16, nextBlink: 3 }
  life = stepLife(life, dt, rand, LOOKS[state], state === 'idle' ? 0.4 : 0.15)
  const time = f.time + dt
  const target = targetOf(state, time, life.gaze, calm)
  const springs = { ...f.springs }
  // Small steps keep the springs stable at any frame rate.
  const steps = Math.max(1, Math.ceil(dt / (1 / 120)))
  const h = dt / steps
  for (const k of KEYS) {
    let { x, v } = springs[k]
    const stiff = calm ? 60 : (STIFF[k] ?? 150)
    const damp = 2 * Math.sqrt(stiff) * 0.72
    for (let i = 0; i < steps; i++) {
      v += (stiff * (target[k] - x) - damp * v) * h
      x += v * h
    }
    springs[k] = { x, v }
  }
  const breath = calm ? 0 : Math.sin((time * Math.PI * 2) / 4.6) > 0.2 ? 1 : 0
  return { springs, life, state, time, breath }
}

/** The expression to draw now: the springs, with the blink closing the lids. */
export function expressionOf(f: FaceMotion): Expression {
  const e = Object.fromEntries(KEYS.map((k) => [k, f.springs[k].x])) as unknown as Expression
  e.lid *= blinkOpen(f.life)
  return e
}
