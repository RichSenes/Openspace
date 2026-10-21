import { describe, expect, it } from 'vitest'
import { between, fit, MAX_SCALE, MIN_SCALE, pan, toWorld, wheelFactor, zoomAt } from './camera'

const viewport = { width: 1000, height: 600 }

describe('fit', () => {
  it('centres the target with a whole-number zoom when it fits', () => {
    const camera = fit({ x: 0, y: 0, width: 200, height: 100 }, viewport)
    expect(camera.scale).toBe(4)
    expect(camera.x).toBe(100)
    expect(camera.y).toBe(100)
  })

  it('shrinks below 1 only when it must, and centres an offset target', () => {
    const camera = fit({ x: 100, y: 50, width: 2000, height: 500 }, viewport)
    expect(camera.scale).toBeCloseTo(0.484, 3)
    // Positions are rounded to whole screen pixels to keep pixel art crisp: at this
    // zoom that can move the centre by up to about two world units.
    const centre = toWorld(camera, { x: 500, y: 300 })
    expect(Math.abs(centre.x - 1100)).toBeLessThan(2)
    expect(Math.abs(centre.y - 300)).toBeLessThan(2)
  })
})

describe('zoomAt', () => {
  it('keeps the point under the cursor still', () => {
    const camera = { x: 40, y: 20, scale: 2 }
    const cursor = { x: 300, y: 200 }
    const before = toWorld(camera, cursor)
    const after = toWorld(zoomAt(camera, 1.5, cursor), cursor)
    expect(after.x).toBeCloseTo(before.x)
    expect(after.y).toBeCloseTo(before.y)
  })

  it('stays within the zoom limits', () => {
    expect(zoomAt({ x: 0, y: 0, scale: 5 }, 10, { x: 0, y: 0 }).scale).toBe(MAX_SCALE)
    expect(zoomAt({ x: 0, y: 0, scale: 0.5 }, 0.01, { x: 0, y: 0 }).scale).toBe(MIN_SCALE)
  })
})

describe('pan, between and wheelFactor', () => {
  it('moves by the drag distance', () => {
    expect(pan({ x: 10, y: 10, scale: 2 }, 5, -3)).toEqual({ x: 15, y: 7, scale: 2 })
  })

  it('goes from one camera to the other', () => {
    const from = { x: 0, y: 0, scale: 1 }
    const to = { x: 100, y: 50, scale: 3 }
    expect(between(from, to, 0)).toEqual(from)
    expect(between(from, to, 1)).toEqual(to)
  })

  it('zooms in when scrolling up and out when scrolling down, gently', () => {
    expect(wheelFactor(-100)).toBeGreaterThan(1)
    expect(wheelFactor(100)).toBeLessThan(1)
    expect(wheelFactor(-100000)).toBeCloseTo(wheelFactor(-100))
  })
})
