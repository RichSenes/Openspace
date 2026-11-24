import { describe, expect, it } from 'vitest'
import { box, iso, shades } from './iso'
import { Painter } from './px'
import { advance, behind, type Person } from './scene'
import { FOOT_Y, facingOf, H, paintScientist, W } from './scientist'

describe('iso', () => {
  it('runs two pixels across for one down, tiles are 32 by 16', () => {
    expect(iso(8, 0)).toEqual([16, 8])
    expect(iso(0, 8)).toEqual([-16, 8])
    expect(iso(8, 8)).toEqual([0, 16])
    expect(iso(0, 0, 10)).toEqual([0, -10])
  })

  it('paints a box whose faces neither overlap nor leave gaps', () => {
    const p = new Painter(80, 80)
    const at = (a: number, b: number, z = 0) => {
      const [x, y] = iso(a, b, z)
      return [40 + x, 30 + y] as [number, number]
    }
    const faces = box(p, at, 0, 0, 0, 10, 6, 12, shades('#808080'), false)
    for (const i of faces.top) {
      expect(faces.left.has(i)).toBe(false)
      expect(faces.right.has(i)).toBe(false)
    }
    for (const i of faces.left) expect(faces.right.has(i)).toBe(false)
    // Every pixel between the left and right faces' outer edges, on one row, is painted.
    const row = 30 + 10
    const xs = [...faces.left, ...faces.right].filter((i) => Math.floor(i / 80) === row).map((i) => i % 80)
    const lo = Math.min(...xs)
    const hi = Math.max(...xs)
    for (let x = lo; x <= hi; x++) expect(p.alpha(x, row)).toBe(255)
  })
})

describe('scientist', () => {
  it('stands on its foot pixel and fits its frame in every pose', () => {
    for (const back of [false, true]) {
      for (const pose of ['stand', 'walk', 'notes', 'think', 'coffee', 'type'] as const) {
        const p = paintScientist({ skin: '#f1c9a5', hair: '#1d1a22', style: 'long', shirt: '#5b9bff', glasses: true }, back, pose, 1)
        let top = H
        let bottom = 0
        for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (p.alpha(x, y) > 0) {
          top = Math.min(top, y)
          bottom = Math.max(bottom, y)
        }
        expect(top).toBeGreaterThan(0)
        expect(bottom).toBeLessThanOrEqual(FOOT_Y + 1)
        expect(bottom - top).toBeGreaterThan(40)
      }
    }
  })

  it('faces the way it walks', () => {
    expect(facingOf(1, 0)).toBe('SE')
    expect(facingOf(0, 1)).toBe('SW')
    expect(facingOf(-1, 0)).toBe('NW')
    expect(facingOf(0, -1)).toBe('NE')
  })
})

describe('scene', () => {
  const person = (route: Person['route']): Person => ({
    look: { skin: '#000000', hair: '#000000', style: 'short', shirt: '#000000' },
    frames: { get: () => document.createElement('canvas') },
    a: 0,
    b: 0,
    facing: 'SE',
    pose: 'stand',
    route,
    step: 0,
    into: 0,
    seed: 0,
  })

  it('walks along one axis then the other, then acts', () => {
    const p = person([{ go: [10, 6] }, { act: 'notes', face: 'NW', secs: 2, bubble: 'question' }])
    advance(p, 0.4)
    expect(p.a).toBeCloseTo(6)
    expect(p.b).toBe(0)
    expect(p.facing).toBe('SE')
    advance(p, 0.4)
    expect(p.a).toBe(10)
    expect(p.b).toBeCloseTo(2)
    expect(p.facing).toBe('SW')
    advance(p, 0.5)
    expect(p.pose).toBe('notes')
    expect(p.facing).toBe('NW')
    expect(p.bubble).toBe('question')
  })

  it('goes back to the start of the route when done', () => {
    const p = person([{ act: 'think', face: 'SE', secs: 1 }, { act: 'type', face: 'NE', secs: 1 }])
    advance(p, 1.5)
    expect(p.pose).toBe('type')
    advance(p, 1)
    expect(p.pose).toBe('think')
  })

  it('draws what stands in front after what stands behind', () => {
    const desk = { a0: 10, b0: 10, a1: 50, b1: 20 }
    expect(behind({ a0: 20, b0: 24, a1: 22, b1: 26 }, desk)).toBeGreaterThan(0)
    expect(behind({ a0: 20, b0: 4, a1: 22, b1: 6 }, desk)).toBeLessThan(0)
    expect(behind({ a0: 2, b0: 14, a1: 4, b1: 16 }, desk)).toBeLessThan(0)
  })
})
