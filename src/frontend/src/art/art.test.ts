import { describe, expect, it } from 'vitest'
import { CHARACTER_COLORS, CLOTH_COLORS, HAIR_COLORS, MOUTHS, SKIN_TONES, THEMES, type ClothColor, type HairColor, type SkinTone } from './palette'
import { AGENT_LOOKS, poseOf, type AgentState } from './agentStates'
import { baseline, scenePixels } from './scene'
import { ACCESSORIES, BODY, FACES, GESTURES, HAIR_STYLES, HANDS, HEAD } from './sheets/character'
import { PROPS } from './sheets/props'
import { DESK, FLOOR, WALL } from './sheets/room'
import { SIGNALS } from './sheets/signals'
import { problemsOf, pixelsOf, type SpriteDef } from './sprite'
import { characterColors, flashing, GESTURE_PARTS, STATION, stationPixels, themeColors, type Gesture } from './station'

const characterSprites: SpriteDef[] = [
  HEAD,
  BODY,
  ...Object.values(FACES),
  ...Object.values(HAIR_STYLES),
  ...Object.values(HANDS),
  ...Object.values(GESTURES),
  ...Object.values(ACCESSORIES),
]
const roomSprites: SpriteDef[] = [FLOOR, WALL, DESK, ...Object.values(SIGNALS), ...Object.values(PROPS)]

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

describe('themes', () => {
  it.each(Object.entries(THEMES))('%s gives every name a colour', (_, theme) => {
    for (const color of Object.values(theme)) expect(color).toMatch(/^#[0-9a-f]{6}$/)
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
    const pixels = stationPixels(look, { emotion: 'neutral', gesture: 'rest', frame: 0, screen: 'on' }, THEMES.cozy)
    expect(pixels.every((p) => p.x >= 0 && p.x < STATION.width && p.y >= 0 && p.y < STATION.height)).toBe(true)
    // The desk top spans desk rows 11 to 13; resting hands show skin on its middle row.
    const handOnDesk = pixels.filter((p) => p.y === STATION.deskY + 12 && p.color === '#f6d5bc')
    expect(handOnDesk.length).toBeGreaterThan(0)
    expect(pixels.findLastIndex((p) => p.color === '#f6d5bc')).toBeGreaterThan(pixels.findIndex((p) => p.color === THEMES.cozy['wood.light']))
  })

  it('keeps the tallest hair style whole, below the room for signals', () => {
    const bun = stationPixels({ ...look, hairStyle: 'bun' }, { emotion: 'neutral', gesture: 'rest', frame: 0, screen: 'on' }, THEMES.cozy)
    expect(Math.min(...bun.map((p) => p.y))).toBe(11)
  })

  it('draws a signal above the head without leaving the station', () => {
    for (const signal of Object.keys(SIGNALS) as (keyof typeof SIGNALS)[]) {
      const pixels = stationPixels({ ...look, hairStyle: 'bun' }, { emotion: 'neutral', gesture: 'rest', frame: 0, screen: 'on', signal }, THEMES.cozy)
      expect(Math.min(...pixels.map((p) => p.y)), signal).toBeGreaterThanOrEqual(0)
    }
  })

  it('spills the screen light on the desk only while the screen is on', () => {
    const spill = (screen: 'off' | 'on') =>
      stationPixels(look, { emotion: 'neutral', gesture: 'rest', frame: 0, screen }, THEMES.cozy).some((p) => p.color === THEMES.cozy['spill.on'])
    expect([spill('on'), spill('off')]).toEqual([true, false])
  })

  it('flashes an alert screen and leaves other states alone', () => {
    expect([0, 1, 2, 3].map((f) => flashing('alert', f))).toEqual(['alert', 'alert', 'off', 'off'])
    expect(flashing('on', 3)).toBe('on')
  })

  it('lights the screen according to its state', () => {
    const colors = (screen: 'off' | 'on' | 'alert') =>
      new Set(stationPixels(look, { emotion: 'neutral', gesture: 'rest', frame: 0, screen }, THEMES.cozy).map((p) => p.color))
    expect(colors('on').has(THEMES.cozy['screen.on'])).toBe(true)
    expect(colors('alert').has(THEMES.cozy['screen.alert'])).toBe(true)
    expect(colors('off').has(THEMES.cozy['screen.on'])).toBe(false)
  })

  it('keeps every gesture and accessory inside the station', () => {
    for (const gesture of Object.keys(GESTURE_PARTS) as Gesture[])
      for (const accessory of [undefined, ...(Object.keys(ACCESSORIES) as (keyof typeof ACCESSORIES)[])])
        for (const frame of [0, 1]) {
          const pixels = stationPixels({ ...look, accessory }, { emotion: 'neutral', gesture, frame, screen: 'on' }, THEMES.cozy)
          expect(pixels.every((p) => p.x >= 0 && p.x < STATION.width && p.y >= 0 && p.y < STATION.height), `${gesture} ${accessory}`).toBe(true)
        }
  })

  it('keeps raised arms behind the monitor', () => {
    // Where the left forearm of 'stuck' crosses the screen's lit edge, the screen wins.
    const pixels = stationPixels(look, { emotion: 'worried', gesture: 'stuck', frame: 0, screen: 'on' }, THEMES.cozy)
    const last = pixels.findLast((p) => p.x === 12 && p.y === STATION.deskY + 2)
    expect(last?.color).toBe(THEMES.cozy['screen.on'])
  })

  it('raises a hand while waiting and keeps the other one on the desk', () => {
    const pixels = stationPixels(look, poseOf('waiting', 0), THEMES.cozy)
    const skin = SKIN_TONES.fair[0]
    const top = STATION.deskY - 14
    expect(pixels.some((p) => p.color === skin && p.x >= 8 + 19 && p.y < top + 12)).toBe(true)
    expect(pixels.some((p) => p.color === skin && p.y === STATION.deskY + 12 && p.x < 8 + 12)).toBe(true)
    expect(pixels.some((p) => p.color === skin && p.y === STATION.deskY + 12 && p.x >= 8 + 12)).toBe(false)
  })

  it('places a sprite where it is told', () => {
    expect(pixelsOf(FLOOR, 0, themeColors(THEMES.cozy), 16, 32)[0]).toMatchObject({ x: 16, y: 32 })
  })
})

describe('agent states', () => {
  it.each(Object.keys(AGENT_LOOKS) as AgentState[])('%s blinks now and then unless asleep', (state) => {
    const faces = Array.from({ length: 40 }, (_, t) => poseOf(state, t).emotion)
    expect(faces.includes('blink')).toBe(AGENT_LOOKS[state].emotion !== 'asleep')
  })

  it('shows the state signal unless a momentary one replaces it', () => {
    expect(poseOf('waiting', 0).signal).toBe('question')
    expect(poseOf('working', 0).signal).toBeUndefined()
    expect(poseOf('working', 0, 'bulb').signal).toBe('bulb')
  })

  it('flashes the screen while waiting for a person', () => {
    expect([0, 2].map((t) => poseOf('waiting', t).screen)).toEqual(['alert', 'off'])
  })
})

describe('scenePixels', () => {
  const dot = (color: string) => [{ x: 0, y: 0, color }]

  it('draws what stands lower on the floor last, so it covers what is behind', () => {
    const front = { x: 5, y: 20, height: 10, pixels: dot('front') }
    const back = { x: 5, y: 0, height: 25, pixels: dot('back') }
    expect(scenePixels([front, back]).map((p) => p.color)).toEqual(['back', 'front'])
    expect(baseline(front)).toBe(30)
  })

  it('moves every pixel to where its item stands', () => {
    expect(scenePixels([{ x: 3, y: 4, height: 1, pixels: dot('a') }])[0]).toMatchObject({ x: 3, y: 4 })
  })
})
