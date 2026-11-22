import { describe, expect, it } from 'vitest'
import { hologram, HOLOGRAM, SCAN_EVERY, SHIORI_COLORS, SHIORI_FRAME, shioriBubble, shioriPixels } from './shiori'
import * as sheets from './sheets/shiori'
import { problemsOf, type SpriteDef } from './sprite'

const sprites = Object.values(sheets).filter((v): v is SpriteDef => typeof v === 'object' && 'frames' in v)
const colorsAt = (pixels: { x: number; y: number; color: string }[], y: number) => new Set(pixels.filter((p) => p.y === y).map((p) => p.color))

describe("Shiori's sprites", () => {
  it('are well formed and every letter has a colour', () => {
    for (const sprite of sprites) {
      expect(problemsOf(sprite)).toEqual([])
      for (const name of Object.values(sprite.legend)) expect(SHIORI_COLORS[name] ?? (name === 'chat.paper' ? 'ok' : undefined), name).toBeDefined()
    }
  })

  it('shows her pins and sash from the front, behind and across', () => {
    for (const facing of ['front', 'back', 'right', 'left'] as const) {
      const pixels = shioriPixels({ facing, step: 0, mode: 'walking' }, 0)
      const all = new Set(pixels.map((p) => p.color))
      expect(all.has(SHIORI_COLORS['pin.gold']), facing).toBe(true)
      expect(all.has(SHIORI_COLORS['sash.light']), facing).toBe(true)
      expect(pixels.every((p) => p.x >= 0 && p.x < 24 && p.y >= 0 && p.y < 32)).toBe(true)
    }
  })

  it('mirrors walking left, steps with her legs and explains with her hand while talking', () => {
    const right = shioriPixels({ facing: 'right', step: 0, mode: 'walking' }, 0)
    const left = shioriPixels({ facing: 'left', step: 0, mode: 'walking' }, 0)
    expect(left.map((p) => 23 - p.x).sort()).toEqual(right.map((p) => p.x).sort())
    const stride = shioriPixels({ facing: 'right', step: 1, mode: 'walking' }, 0)
    expect(stride).not.toEqual(right)
    const talking = shioriPixels({ facing: 'right', step: 0, mode: 'talking' }, 0)
    // The forearm reaches out past the dress, at chest height.
    expect(talking.some((p) => p.x >= 19 && p.y === 20)).toBe(true)
    expect(right.some((p) => p.x >= 19 && p.y === 20)).toBe(false)
  })

  it('glows as a hologram: a thin edge along her outline, a pool at her feet, dimmed with the lights off', () => {
    const body = shioriPixels({ facing: 'front', step: 0, mode: 'idle' }, 0)
    const lit = hologram(body, 5)
    expect(lit.body).toHaveLength(body.length)
    expect(lit.glow.every((p) => p.glow && p.x >= 0 && p.x < SHIORI_FRAME.width && p.y < SHIORI_FRAME.height)).toBe(true)
    expect(colorsAt(lit.glow, 32 + SHIORI_FRAME.y)).toEqual(new Set([HOLOGRAM.floor]))
    const taken = new Set(lit.body.map((p) => `${p.x},${p.y}`))
    expect(lit.glow.some((p) => taken.has(`${p.x},${p.y}`))).toBe(false)
    // Never in a gap with her on both sides, such as between the legs.
    for (const p of lit.glow.filter((g) => g.y < 32 + SHIORI_FRAME.y)) {
      const near = (dx: number, dy: number) => taken.has(`${p.x + dx},${p.y + dy}`) || taken.has(`${p.x + 2 * dx},${p.y + 2 * dy}`)
      expect(near(-1, 0) && near(1, 0)).toBe(false)
      expect(near(0, -1) && near(0, 1)).toBe(false)
    }
    const dark = hologram(body, 5, { night: '#101020' })
    expect(dark.body.some((p, i) => p.color !== lit.body[i].color)).toBe(true)
  })

  it('scans her dress with a faint line only when asked, never her face', () => {
    const body = shioriPixels({ facing: 'front', step: 0, mode: 'idle' }, 0)
    const still = hologram(body, 3)
    expect(still.body.map((p) => p.color)).toEqual(body.map((p) => p.color))
    for (let tick = 0; tick < SCAN_EVERY; tick++) {
      const moving = hologram(body, tick, { scan: true }).body
      const changed = moving.filter((p, i) => p.color !== still.body[i].color)
      expect(changed.every((p) => p.y - SHIORI_FRAME.y >= 17)).toBe(true)
      expect(new Set(changed.map((p) => p.y)).size).toBeLessThanOrEqual(1)
    }
    expect(hologram(body, 3, { scan: true }).body.some((p, i) => p.color !== still.body[i].color)).toBe(true)
  })

  it('brings a different icon for each piece of news', () => {
    const icons = (['started', 'failed', 'waiting', 'review'] as const).map((e) => JSON.stringify(shioriBubble(e)))
    expect(new Set(icons).size).toBe(4)
  })
})
