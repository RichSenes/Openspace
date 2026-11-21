import { describe, expect, it } from 'vitest'
import { DEFAULT_LOOK, parseLook } from './look'

describe('parseLook', () => {
  it('falls back on the defaults for nothing or garbage', () => {
    expect(parseLook(null)).toEqual(DEFAULT_LOOK)
    expect(parseLook('not json')).toEqual(DEFAULT_LOOK)
    expect(parseLook('42')).toEqual(DEFAULT_LOOK)
  })

  it('keeps known choices and drops unknown ones', () => {
    const look = parseLook(JSON.stringify({ skin: 'medusa', pixel: 0, hour: 'night', sky: 'Rain', world: 'space', calm: true }))
    expect(look).toEqual({ skin: 'medusa', pixel: 0, hour: 'night', sky: 'Rain', world: 'space', calm: true })
    expect(parseLook(JSON.stringify({ skin: 'scanline', pixel: 7, world: 'kyoto', hour: 'noon' }))).toEqual(DEFAULT_LOOK)
  })
})
