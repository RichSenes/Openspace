import { describe, expect, it } from 'vitest'
import { doorwayPixels, GLASS, glazed, phaseOf, skyPixels, windowLight, windowPixels, WINDOW_SIZE } from './windows'

const colors = (name: string) => ({ outline: '#000000', 'trim.light': '#aaaaaa', 'trim.dark': '#555555' })[name] ?? null

describe('the sky', () => {
  it('follows the time of day', () => {
    expect([phaseOf(3), phaseOf(7), phaseOf(12), phaseOf(19), phaseOf(23)]).toEqual(['night', 'dusk', 'day', 'dusk', 'night'])
  })

  it('fills the glass, shines by itself and moves', () => {
    const now = skyPixels(0, GLASS.width, GLASS.height, 0, 'day')
    const later = skyPixels(0, GLASS.width, GLASS.height, 30, 'day')
    expect(now).toHaveLength(GLASS.width * GLASS.height)
    expect(now.every((p) => p.glow)).toBe(true)
    expect(later.map((p) => p.color)).not.toEqual(now.map((p) => p.color))
  })

  it('is one sky across neighbouring windows', () => {
    const left = skyPixels(100, 10, 16, 0, 'night')
    const both = skyPixels(100, 20, 16, 0, 'night')
    expect(both.filter((p) => p.x < 10).map((p) => p.color)).toEqual(left.map((p) => p.color))
  })

  it('lights a dark room by day more than by night', () => {
    expect(windowLight('day')[0].radius).toBeGreaterThan(windowLight('night')[0].radius)
  })
})

describe('windowPixels', () => {
  it('frames the view, covering it where the cross and the frame are', () => {
    const view = Array.from({ length: GLASS.width * GLASS.height }, (_, i) => ({ x: i % GLASS.width, y: Math.floor(i / GLASS.width), color: '#123456' }))
    const pixels = windowPixels(view, colors)
    expect(new Set(pixels.map((p) => `${p.x},${p.y}`)).size).toBe(pixels.length)
    expect(pixels.every((p) => p.x >= 0 && p.y >= 0 && p.x < WINDOW_SIZE.width && p.y < WINDOW_SIZE.height)).toBe(true)
    expect(pixels.find((p) => p.x === 0 && p.y === 0)?.color).toBe('#000000')
    expect(pixels.find((p) => p.x === 5 && p.y === 6)?.color).toBe(glazed('#123456'))
  })
})

describe('doorwayPixels', () => {
  it('is a dark opening without a view and shows the view when there is one', () => {
    expect(doorwayPixels(undefined, colors).find((p) => p.x === 15 && p.y === 20)?.color).toBe('#000000')
    const view = [{ x: 12, y: 15, color: '#ff0000' }]
    expect(doorwayPixels(view, colors).find((p) => p.x === 15 && p.y === 20)?.color).toBe('#ff0000')
  })
})

describe('glazed', () => {
  it('pales a colour towards a light blue, a little', () => {
    expect(glazed('#000000')).toBe('#2e3338')
    expect(glazed('#cfe6ff')).toBe('#cfe6ff')
  })
})
