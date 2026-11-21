import { describe, expect, it } from 'vitest'
import { NEUTRAL } from './expression'
import { curve } from './paint'
import { faceGeometry, PORTRAIT_H, PORTRAIT_W, RAMPS } from './portrait'

describe('the portrait', () => {
  it('places two eyes either side of the face, the mouth below them', () => {
    const f = faceGeometry(NEUTRAL)
    expect(f.eyes[0].x).toBeLessThan(f.cx)
    expect(f.eyes[1].x).toBeGreaterThan(f.cx)
    expect(f.mouth.y).toBeGreaterThan(f.nose.y)
    expect(f.nose.y).toBeGreaterThan(f.eyes[0].y)
  })

  it('turns the face, the near features moving more than the outline', () => {
    const turned = faceGeometry({ ...NEUTRAL, turn: 1 })
    const still = faceGeometry(NEUTRAL)
    expect(turned.nose.x - still.nose.x).toBeGreaterThan(turned.cx - still.cx)
  })

  it('fits a dense canvas, with ramps of a few tones each', () => {
    expect(PORTRAIT_W).toBeGreaterThanOrEqual(128)
    expect(PORTRAIT_H).toBeGreaterThan(PORTRAIT_W)
    for (const r of Object.values(RAMPS)) {
      expect(r.length).toBeGreaterThanOrEqual(3)
      expect(r.length).toBeLessThanOrEqual(5)
    }
  })
})

describe('pixel curves', () => {
  it('leave no gaps on steep slopes', () => {
    const pts = curve((x) => x * 3, 0, 4)
    for (let i = 1; i < pts.length; i++) expect(Math.abs(pts[i][1] - pts[i - 1][1])).toBeLessThanOrEqual(1)
    expect(pts[pts.length - 1]).toEqual([4, 12])
  })
})
