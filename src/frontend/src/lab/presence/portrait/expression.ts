/**
 * What Shiori's face is doing: every part of the portrait reads these numbers. Expressions are
 * made of the face's own parts (lids, brows, mouth, gaze, a tilt of the head), never of the
 * whole body jumping.
 */
export interface Expression {
  /** Upper lid: 1 open, 0 shut. */
  lid: number
  /** Lower lid raised by a smile or a squint, 0..1. */
  cheek: number
  /** Brows: -1 lowered and drawn together, 0 rest, 1 raised. */
  brow: number
  /** One brow higher than the other (doubt), -1..1. */
  browTilt: number
  /** Mouth corners: -1 down, 0 rest, 1 a smile. */
  smile: number
  /** Mouth open, 0..1 (speech, surprise). */
  open: number
  /** Mouth width for vowels: -1 rounded (o, u), 1 wide (e, i). */
  wide: number
  /** Where she looks, -1..1 (x to her left as you see it, y down). */
  gazeX: number
  gazeY: number
  /** Head turn, -1..1, and tilt in radians. */
  turn: number
  tilt: number
}

export const NEUTRAL: Expression = { lid: 1, cheek: 0, brow: 0, browTilt: 0, smile: 0.15, open: 0, wide: 0, gazeX: 0, gazeY: 0, turn: 0, tilt: 0 }
