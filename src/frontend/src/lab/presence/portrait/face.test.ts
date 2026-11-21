import { describe, expect, it } from 'vitest'
import { FACE_STATES } from '../../shiori/faceMachine'
import { rng } from '../body/shapes'
import { EXPRESSIONS, expressionOf, makeFace, stepFace, targetOf } from './face'

const run = (state: Parameters<typeof stepFace>[2], seconds: number, from = makeFace()) => {
  const r = rng(3)
  let f = from
  for (let i = 0; i < seconds * 60; i++) f = stepFace(f, 1 / 60, state, r, false)
  return f
}

describe("Shiori's face", () => {
  it('has an expression for every state', () => {
    for (const s of FACE_STATES) expect(EXPRESSIONS[s]).toBeDefined()
  })

  it('smiles with her eyes when pleased and frowns a little when unsure', () => {
    expect(EXPRESSIONS.pleased.smile).toBeGreaterThan(0.5)
    expect(EXPRESSIONS.pleased.cheek).toBeGreaterThan(0.5)
    expect(EXPRESSIONS.unsure.smile).toBeLessThan(0)
    expect(Math.abs(EXPRESSIONS.unsure.browTilt!)).toBeGreaterThan(0.5)
  })

  it('reaches a new expression and settles there', () => {
    const f = run('pleased', 3)
    const e = expressionOf(f)
    expect(e.smile).toBeGreaterThan(0.85)
    expect(Math.abs(f.springs.smile.v)).toBeLessThan(0.05)
  })

  it('overshoots a little on the way, as a face does', () => {
    const r = rng(1)
    let f = makeFace('idle')
    let peak = -Infinity
    for (let i = 0; i < 120; i++) {
      f = stepFace(f, 1 / 60, 'pleased', r, false)
      peak = Math.max(peak, f.springs.smile.x)
    }
    expect(peak).toBeGreaterThan(1)
    expect(peak).toBeLessThan(1.15)
  })

  it('blinks when her thought changes', () => {
    const r = rng(2)
    let f = makeFace('idle')
    let least = Infinity
    for (let i = 0; i < 12; i++) {
      f = stepFace(f, 1 / 60, 'thinking', r, false)
      least = Math.min(least, expressionOf(f).lid)
    }
    expect(least).toBeLessThan(0.2)
  })

  it('opens and shapes her mouth while speaking, and closes it after', () => {
    let opened = 0
    for (let t = 0; t < 3; t += 0.05) opened = Math.max(opened, targetOf('speaking', t, [0, 0], false).open)
    expect(opened).toBeGreaterThan(0.5)
    expect(targetOf('idle', 1, [0, 0], false).open).toBe(0)
    expect(targetOf('speaking', 1, [0, 0], true).open).toBe(0)
  })

  it('turns her head toward where she looks, less than her eyes', () => {
    const t = targetOf('idle', 0, [1, 0], false)
    expect(t.turn).toBeGreaterThan(0)
    expect(t.turn).toBeLessThan(t.gazeX)
  })

  it('breathes, one pixel at a time', () => {
    const seen = new Set<number>()
    const r = rng(5)
    let f = makeFace()
    for (let i = 0; i < 600; i++) {
      f = stepFace(f, 1 / 60, 'idle', r, false)
      seen.add(f.breath)
    }
    expect([...seen].sort()).toEqual([0, 1])
  })
})
