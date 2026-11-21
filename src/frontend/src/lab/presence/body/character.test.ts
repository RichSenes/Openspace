import { describe, expect, it } from 'vitest'
import { FACE_STATES } from '../../shiori/faceMachine'
import { approach, BLINK, blinkOpen, LOOKS, makeLife, makePoseMotion, POSES, stepLife, stepPose, voiceLevel } from './character'
import { rng } from './shapes'

describe('poses', () => {
  it('cover every face state', () => {
    for (const s of FACE_STATES) {
      expect(POSES[s]).toBeDefined()
      expect(LOOKS[s]).toBeDefined()
    }
  })

  it('dissolve the eyes while thinking and turn them into arcs when pleased', () => {
    expect(POSES.thinking.eyes).toBe(0)
    expect(POSES.pleased.pleased).toBe(1)
    expect(POSES.unsure.unsure).toBe(1)
  })

  it('ease toward a target the same way at any frame rate', () => {
    let a = { ...POSES.idle }
    for (let i = 0; i < 60; i++) a = approach(a, POSES.thinking, 1 / 60)
    let b = { ...POSES.idle }
    for (let i = 0; i < 30; i++) b = approach(b, POSES.thinking, 1 / 30)
    expect(a.speed).toBeCloseTo(b.speed, 6)
    expect(a.speed).toBeGreaterThan(POSES.idle.speed)
  })
})

describe('life between states', () => {
  it('blinks within a few seconds and opens again', () => {
    const r = rng(4)
    let l = makeLife()
    let closed = false
    let reopened = false
    for (let i = 0; i < 60 * 8; i++) {
      l = stepLife(l, 1 / 60, r, [0, 0], 0.3)
      if (blinkOpen(l) < 0.2) closed = true
      if (closed && blinkOpen(l) === 1) reopened = true
    }
    expect(closed).toBe(true)
    expect(reopened).toBe(true)
  })

  it('looks where the state wants, give or take a glance', () => {
    const r = rng(8)
    let l = makeLife()
    for (let i = 0; i < 60 * 5; i++) l = stepLife(l, 1 / 60, r, [0, 0.55], 0.15)
    expect(l.gaze[1]).toBeGreaterThan(0.3)
  })

  it('is open when not blinking and almost shut mid-blink', () => {
    expect(blinkOpen(makeLife())).toBe(1)
    expect(blinkOpen({ ...makeLife(), blink: BLINK * 0.6 })).toBeLessThan(0.1)
  })

  it('gives a voice level between 0 and 1', () => {
    for (let t = 0; t < 5; t += 0.037) {
      const v = voiceLevel(t)
      expect(v).toBeGreaterThanOrEqual(0)
      expect(v).toBeLessThanOrEqual(1)
    }
  })
})

describe('poses on springs', () => {
  it('gives the creatures lids: lowered and slanted when unsure, raised and wide when listening', () => {
    expect(POSES.unsure.lid).toBeGreaterThan(POSES.idle.lid)
    expect(POSES.unsure.lidTilt).toBeGreaterThan(0.5)
    expect(POSES.listening.lid).toBe(0)
    expect(POSES.listening.wide).toBe(1)
  })

  it('moves a little the other way first, then overshoots and settles', () => {
    let m = makePoseMotion('idle')
    const start = m.pose.pleased
    let least = Infinity
    let most = -Infinity
    for (let i = 0; i < 240; i++) {
      m = stepPose(m, 'pleased', 1 / 60, false)
      if (i < 6) least = Math.min(least, m.pose.pleased)
      most = Math.max(most, m.pose.pleased)
    }
    expect(least).toBeLessThan(start)
    expect(most).toBeGreaterThan(1)
    expect(m.pose.pleased).toBeCloseTo(1, 2)
  })

  it('skips the anticipation with calm motion', () => {
    const m = stepPose(makePoseMotion('idle'), 'pleased', 1 / 60, true)
    expect(m.pose.pleased).toBeGreaterThanOrEqual(0)
  })
})
