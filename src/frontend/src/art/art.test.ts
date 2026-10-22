import { describe, expect, it } from 'vitest'
import { CHARACTER_COLORS, CLOTH_COLORS, HAIR_COLORS, MOUTHS, SKIN_TONES, THEMES, type ClothColor, type HairColor, type SkinTone } from './palette'
import { BODY, FACES, HAIR_STYLES, HANDS, HEAD } from './sheets/character'
import { DESK, FLOOR, WALL } from './sheets/room'
import { SIGNALS } from './sheets/signals'
import { problemsOf, pixelsOf, type SpriteDef } from './sprite'
import { characterColors, flashing, STATION, stationPixels, themeColors } from './station'

const characterSprites: SpriteDef[] = [HEAD, BODY, ...Object.values(FACES), ...Object.values(HAIR_STYLES), ...Object.values(HANDS)]
const roomSprites: SpriteDef[] = [FLOOR, WALL, DESK, ...Object.values(SIGNALS)]

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
          for (const name of Object.values(sprite.legend)) {
            // With the screen off the halo is simply not there.
            if (name === 'glow' && screen === 'off') continue
            expect(resolve(name), `${sprite.name}/${name}`).not.toBeNull()
          }
      }
  })
})

/** WCAG relative luminance contrast between two #rrggbb colours. */
function contrast(a: string, b: string): number {
  const lum = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl
  }
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

describe('faces read for everyone', () => {
  it.each(Object.keys(SKIN_TONES) as SkinTone[])('eyes and mouth stand out from %s skin by lightness', (skin) => {
    const face = SKIN_TONES[skin][0]
    expect(contrast(CHARACTER_COLORS.eye, face)).toBeGreaterThanOrEqual(3)
    expect(contrast(MOUTHS[skin], face)).toBeGreaterThanOrEqual(3)
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

  it('keeps the tallest hair style whole, below the room for signals', () => {
    const bun = stationPixels({ ...look, hairStyle: 'bun' }, { emotion: 'neutral', hands: 'rest', frame: 0, screen: 'on' }, THEMES.cozy)
    expect(Math.min(...bun.map((p) => p.y))).toBe(11)
  })

  it('draws a signal above the head without leaving the station', () => {
    for (const signal of Object.keys(SIGNALS) as (keyof typeof SIGNALS)[]) {
      const pixels = stationPixels({ ...look, hairStyle: 'bun' }, { emotion: 'neutral', hands: 'rest', frame: 0, screen: 'on', signal }, THEMES.cozy)
      expect(Math.min(...pixels.map((p) => p.y)), signal).toBeGreaterThanOrEqual(0)
    }
  })

  it('spills the screen light on the desk only while the screen is on', () => {
    const spill = (screen: 'off' | 'on') =>
      stationPixels(look, { emotion: 'neutral', hands: 'rest', frame: 0, screen }, THEMES.cozy).some((p) => p.color === THEMES.cozy['spill.on'])
    expect([spill('on'), spill('off')]).toEqual([true, false])
  })

  it('flashes an alert screen and leaves other states alone', () => {
    expect([0, 1, 2, 3].map((f) => flashing('alert', f))).toEqual(['alert', 'alert', 'off', 'off'])
    expect(flashing('on', 3)).toBe('on')
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
