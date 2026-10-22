import { describe, expect, it } from 'vitest'
import { CLOTH_COLORS, HAIR_COLORS, SKIN_TONES, THEMES, type ClothColor, type HairColor, type SkinTone } from './palette'
import { BODY, FACES, HAIR_STYLES, HANDS, HEAD } from './sheets/character'
import { DESK, FLOOR, WALL } from './sheets/room'
import { problemsOf, pixelsOf, type SpriteDef } from './sprite'
import { characterColors, STATION, stationPixels, themeColors } from './station'

const characterSprites: SpriteDef[] = [HEAD, BODY, ...Object.values(FACES), ...Object.values(HAIR_STYLES), ...Object.values(HANDS)]
const roomSprites: SpriteDef[] = [FLOOR, WALL, DESK]

describe('sprites', () => {
  it.each([...characterSprites, ...roomSprites].map((s) => [s.name, s] as const))('%s is well formed', (_, sprite) => {
    expect(problemsOf(sprite)).toEqual([])
  })

  it('character sprites resolve every colour in every skin, hair and cloth', () => {
    for (const skin of Object.keys(SKIN_TONES) as SkinTone[])
      for (const hair of Object.keys(HAIR_COLORS) as HairColor[])
        for (const cloth of Object.keys(CLOTH_COLORS) as ClothColor[]) {
          const resolve = characterColors({ skin, hair, cloth, hairStyle: 'short' })
          for (const sprite of characterSprites)
            for (const name of Object.values(sprite.legend)) expect(resolve(name), `${sprite.name}/${name}`).not.toBeNull()
        }
  })

  it('room sprites resolve every colour in every theme and screen state', () => {
    for (const theme of Object.values(THEMES))
      for (const screen of ['off', 'on', 'alert'] as const) {
        const resolve = themeColors(theme, screen)
        for (const sprite of roomSprites)
          for (const name of Object.values(sprite.legend)) expect(resolve(name), `${sprite.name}/${name}`).not.toBeNull()
      }
  })
})

describe('problemsOf', () => {
  it('reports wrong sizes and unknown characters', () => {
    const broken: SpriteDef = { name: 'broken', width: 3, height: 2, legend: { a: 'outline', b: 'outline' }, frames: [['aa', 'abx']] }
    expect(problemsOf(broken)).toEqual([
      'broken frame 0 row 0: 2 pixels, expected 3',
      "broken frame 0 row 1: 'x' is not in the legend",
    ])
  })
})

describe('stationPixels', () => {
  const look = { skin: 'fair', hair: 'brown', hairStyle: 'short', cloth: 'blue' } as const

  it('stays inside the station and draws the hands above the desk', () => {
    const pixels = stationPixels(look, { emotion: 'neutral', hands: 'rest', frame: 0, screen: 'on' }, THEMES.cozy)
    expect(pixels.every((p) => p.x >= 0 && p.x < STATION.width && p.y >= 0 && p.y < STATION.height)).toBe(true)
    // The desk top spans desk rows 11 to 13; resting hands show skin on its middle row.
    const handOnDesk = pixels.filter((p) => p.y === STATION.deskY + 12 && p.color === '#f6d5bc')
    expect(handOnDesk.length).toBeGreaterThan(0)
    expect(pixels.findLastIndex((p) => p.color === '#f6d5bc')).toBeGreaterThan(pixels.findIndex((p) => p.color === THEMES.cozy['wood.light']))
  })

  it('keeps the tallest hair style whole', () => {
    const bun = stationPixels({ ...look, hairStyle: 'bun' }, { emotion: 'neutral', hands: 'rest', frame: 0, screen: 'on' }, THEMES.cozy)
    expect(Math.min(...bun.map((p) => p.y))).toBe(0)
  })

  it('lights the screen according to its state', () => {
    const colors = (screen: 'off' | 'on' | 'alert') =>
      new Set(stationPixels(look, { emotion: 'neutral', hands: 'rest', frame: 0, screen }, THEMES.cozy).map((p) => p.color))
    expect(colors('on').has(THEMES.cozy['screen.on'])).toBe(true)
    expect(colors('alert').has(THEMES.cozy['screen.alert'])).toBe(true)
    expect(colors('off').has(THEMES.cozy['screen.on'])).toBe(false)
  })

  it('places a sprite where it is told', () => {
    expect(pixelsOf(FLOOR, 0, themeColors(THEMES.cozy), 16, 32)[0]).toMatchObject({ x: 16, y: 32 })
  })
})
