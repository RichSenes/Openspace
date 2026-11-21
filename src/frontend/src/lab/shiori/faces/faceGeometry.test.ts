import { describe, expect, it } from 'vitest'
import { BAYER4, bendPoint, EYE_H, EYE_W, eyeMask, makeWeb, quadAt } from './faceGeometry'

const filled = (rows: number[][]) => rows.reduce((n, r) => n + r.filter(Boolean).length, 0)

describe('pixel eyes', () => {
  it('fills the whole eye but its corners when wide open', () => {
    const eye = eyeMask(1)
    expect(eye).toHaveLength(EYE_H)
    expect(eye.every((r) => r.length === EYE_W)).toBe(true)
    expect(filled(eye)).toBe(EYE_W * EYE_H - 4)
  })

  it('narrows from the top and closes to a single line, never to nothing', () => {
    expect(filled(eyeMask(0.5))).toBeLessThan(filled(eyeMask(1)))
    const closed = eyeMask(0)
    expect(filled(closed)).toBe(EYE_W - 2)
    expect(closed.at(-1)!.filter(Boolean)).toHaveLength(EYE_W - 2)
  })

  it('draws a happy eye as an arc', () => {
    const happy = eyeMask(1, 'happy')
    expect(filled(happy)).toBe(4 + 4 + 4)
  })

  it('grows taller when listening', () => {
    expect(eyeMask(1, 'open', 0, 2)).toHaveLength(EYE_H + 2)
  })

  it('dithers with sixteen distinct thresholds', () => {
    expect(new Set(BAYER4.flat()).size).toBe(16)
  })
})

describe('ink web', () => {
  it('is the same drawing for the same seed', () => {
    expect(makeWeb(20, 3)).toEqual(makeWeb(20, 3))
  })

  it('joins every node, has no edge twice and stays in the disc', () => {
    const web = makeWeb(22, 7)
    expect(web.nodes).toHaveLength(22)
    web.nodes.forEach((_, i) => expect(web.edges.some((e) => e.a === i || e.b === i)).toBe(true))
    const keys = web.edges.map((e) => [e.a, e.b].sort().join('-'))
    expect(new Set(keys).size).toBe(keys.length)
    web.nodes.forEach((n) => expect(Math.hypot(n.x, n.y)).toBeLessThanOrEqual(1))
  })

  it('bends a stroke sideways and walks along it', () => {
    expect(bendPoint(0, 0, 2, 0, 0)).toEqual([1, 0])
    expect(bendPoint(0, 0, 2, 0, 0.5)).toEqual([1, 1])
    expect(quadAt(0, 0, 1, 1, 2, 0, 0.5)).toEqual([1, 0.5])
  })
})
