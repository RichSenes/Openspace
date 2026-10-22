import { describe, expect, it } from 'vitest'
import { BONE, CHARACTER_COLORS, CLOTH_COLORS, HAIR_COLORS, MOUTHS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone } from './palette'
import { AGENT_LOOKS, poseOf, type AgentState } from './agentStates'
import { FAN_MODULES, withFanPack, type FanModule } from './fanPack'
import { inTheDark } from './lighting'
import { baseline, sceneLights, scenePixels } from './scene'
import { ACCESSORIES, BODY, FACES, GESTURES, HAIR_STYLES, HANDS, HEAD, COSTUMES, OUTFITS } from './sheets/character'
import { PROPS } from './sheets/props'
import { SIGNALS } from './sheets/signals'
import { problemsOf, pixelsOf, type SpriteDef } from './sprite'
import { characterColors, flashing, GESTURE_PARTS, STATION, stationLights, stationPixels, themeColors, type Gesture } from './station'
import { THEMES } from './themes'

const characterSprites: SpriteDef[] = [
  HEAD,
  BODY,
  ...Object.values(FACES),
  ...Object.values(HAIR_STYLES),
  ...Object.values(HANDS),
  ...Object.values(GESTURES),
  ...Object.values(ACCESSORIES),
  ...Object.values(OUTFITS),
  ...Object.values(COSTUMES).flatMap((c) => Object.values(c)),
]
const roomSprites: SpriteDef[] = [
  ...Object.values(THEMES).flatMap((t) => [t.floor, t.wall, t.desk]),
  ...Object.values(SIGNALS),
  ...Object.values(PROPS).map((p) => p.sprite),
]

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
        const resolve = themeColors(theme.colors, screen)
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
    for (const color of Object.values(theme.colors)) expect(color).toMatch(/^#[0-9a-f]{6}$/)
  })

  it.each(Object.entries(THEMES))('%s has a desk the same size, with its top where hands rest', (_, theme) => {
    expect([theme.desk.width, theme.desk.height]).toEqual([32, 21])
    for (const row of [11, 12, 13]) expect(theme.desk.frames[0][row].slice(13, 27)).not.toContain('.')
  })

  it.each(Object.entries(THEMES))('%s only uses furniture that exists', (_, theme) => {
    for (const name of theme.furniture) expect(PROPS[name]).toBeDefined()
  })
})

describe('fan pack', () => {
  const sprite: SpriteDef = { name: 'copy', width: 1, height: 1, legend: { a: 'fan.copy.a' }, frames: [['a']] }

  it('redraws a prop and keeps its line, dropping lights placed for the old drawing', () => {
    const fan: FanModule = { colors: { 'fan.copy.a': '#123456' }, props: { dragonLamp: { sprite } } }
    const merged = withFanPack(PROPS, [fan])
    expect(merged.dragonLamp.sprite).toBe(sprite)
    expect(merged.dragonLamp.hint).toBe(PROPS.dragonLamp.hint)
    expect(merged.dragonLamp.lights).toBeUndefined()
    expect(merged.plant).toBe(PROPS.plant)
  })

  it('ignores props that do not exist', () => {
    const fan = { colors: {}, props: { nothing: { sprite } } } as unknown as FanModule
    expect(Object.keys(withFanPack(PROPS, [fan]))).toEqual(Object.keys(PROPS))
  })

  it('has well formed drawings that resolve, if this machine has one', () => {
    const resolve = themeColors(THEMES.cozy.colors)
    const sprites = FAN_MODULES.flatMap((m) => [
      ...Object.values(m.props).map((p) => p!.sprite),
      ...Object.values(m.costumes ?? {}).flatMap((c) => Object.values(c!)),
    ])
    for (const sprite of sprites) {
      expect(problemsOf(sprite)).toEqual([])
      for (const name of Object.values(sprite.legend)) expect(resolve(name), name).not.toBeNull()
    }
  })
})

describe('furniture', () => {
  it.each(Object.entries(PROPS))('%s has a line to show and lights that resolve', (_, prop) => {
    expect(prop.hint.length).toBeGreaterThan(0)
    for (const light of prop.lights ?? []) {
      expect(themeColors(THEMES.cozy.colors)(light.color)).not.toBeNull()
      expect(light.x).toBeLessThan(prop.sprite.width)
      expect(light.y).toBeLessThan(prop.sprite.height)
    }
  })
})

describe('costumes and outfits', () => {
  const look = { skin: 'deep', hair: 'black', hairStyle: 'long', cloth: 'red' } as const

  it('dresses a skeleton in bone, whatever the skin, and closes its eyes to blink', () => {
    const colors = (emotion: 'neutral' | 'blink') =>
      new Set(stationPixels({ ...look, costume: 'skeleton' }, { emotion, gesture: 'rest', frame: 0, screen: 'on' }, THEMES.cozy).map((p) => p.color))
    expect(colors('neutral').has(BONE[0])).toBe(true)
    expect(colors('neutral').has(SKIN_TONES.deep[0])).toBe(false)
    expect(colors('neutral').has(CHARACTER_COLORS.pupil)).toBe(true)
    expect(colors('blink').has(CHARACTER_COLORS.pupil)).toBe(false)
  })

  it('hides the face under a hood, with eyes that glow in the dark and close to blink', () => {
    const pixels = (emotion: 'neutral' | 'blink') =>
      stationPixels({ ...look, costume: 'hooded' }, { emotion, gesture: 'rest', frame: 0, screen: 'on' }, THEMES.cozy)
    const open = pixels('neutral')
    expect(open.some((p) => p.color === SKIN_TONES.deep[0])).toBe(false)
    expect(open.find((p) => p.color === CHARACTER_COLORS['eye.glow'])?.glow).toBe(true)
    expect(pixels('blink').some((p) => p.color === CHARACTER_COLORS['eye.glow'])).toBe(false)
  })

  it('keeps the skeleton eyes readable on bone', () => {
    expect(contrast(CHARACTER_COLORS.eye, BONE[0])).toBeGreaterThanOrEqual(3)
  })

  it('wears an outfit in place of the shirt', () => {
    const coat = stationPixels({ ...look, outfit: 'coat' }, { emotion: 'neutral', gesture: 'rest', frame: 0, screen: 'on' }, THEMES.cozy)
    expect(coat.some((p) => p.color === CHARACTER_COLORS.zip)).toBe(true)
    expect(coat.some((p) => p.color === CLOTH_COLORS.red[0])).toBe(false)
  })
})

describe('lights', () => {
  const night = THEMES.cozy.colors.night
  const lum = (hex: string) => [1, 3, 5].reduce((sum, i) => sum + parseInt(hex.slice(i, i + 2), 16), 0)

  it('dims what does not glow, less so near a light', () => {
    const pixels = [
      { x: 0, y: 0, color: '#c08a5c' },
      { x: 50, y: 0, color: '#c08a5c' },
      { x: 1, y: 0, color: '#8fe0c4', glow: true as const },
    ]
    const [near, far, screen] = inTheDark(pixels, [{ x: 0, y: 0, radius: 20, color: '#ffffff' }], night)
    expect(lum(far.color)).toBeLessThan(lum('#c08a5c'))
    expect(lum(near.color)).toBeGreaterThan(lum(far.color))
    expect(screen.color).toBe('#8fe0c4')
  })

  it('marks screens and signals as glowing', () => {
    const pixels = stationPixels(
      { skin: 'fair', hair: 'brown', hairStyle: 'short', cloth: 'blue' },
      { emotion: 'neutral', gesture: 'rest', frame: 0, screen: 'on', signal: 'bulb' },
      THEMES.cozy,
    )
    expect(pixels.filter((p) => p.glow).map((p) => p.color)).toContain(THEMES.cozy.colors['screen.on'])
    expect(pixels.find((p) => p.color === '#f6d35a')?.glow).toBe(true)
  })

  it('lights a desk only while its screen is on', () => {
    const pose = { emotion: 'neutral', gesture: 'rest', frame: 0 } as const
    expect(stationLights({ ...pose, screen: 'off' }, THEMES.cozy)).toEqual([])
    expect(stationLights({ ...pose, screen: 'alert' }, THEMES.cozy)[0].color).toBe(THEMES.cozy.colors['screen.alert'])
  })

  it('moves item lights to where the item stands', () => {
    const lights = sceneLights([{ x: 10, y: 20, height: 5, pixels: [], lights: [{ x: 1, y: 2, radius: 3, color: '#ffffff' }] }])
    expect(lights[0]).toMatchObject({ x: 11, y: 22 })
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
    expect(pixels.findLastIndex((p) => p.color === '#f6d5bc')).toBeGreaterThan(pixels.findIndex((p) => p.color === THEMES.cozy.colors['wood.light']))
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
      stationPixels(look, { emotion: 'neutral', gesture: 'rest', frame: 0, screen }, THEMES.cozy).some((p) => p.color === THEMES.cozy.colors['spill.on'])
    expect([spill('on'), spill('off')]).toEqual([true, false])
  })

  it('flashes an alert screen and leaves other states alone', () => {
    expect([0, 1, 2, 3].map((f) => flashing('alert', f))).toEqual(['alert', 'alert', 'off', 'off'])
    expect(flashing('on', 3)).toBe('on')
  })

  it('lights the screen according to its state', () => {
    const colors = (screen: 'off' | 'on' | 'alert') =>
      new Set(stationPixels(look, { emotion: 'neutral', gesture: 'rest', frame: 0, screen }, THEMES.cozy).map((p) => p.color))
    expect(colors('on').has(THEMES.cozy.colors['screen.on'])).toBe(true)
    expect(colors('alert').has(THEMES.cozy.colors['screen.alert'])).toBe(true)
    expect(colors('off').has(THEMES.cozy.colors['screen.on'])).toBe(false)
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
    expect(last?.color).toBe(THEMES.cozy.colors['screen.on'])
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
    expect(pixelsOf(THEMES.cozy.floor, 0, themeColors(THEMES.cozy.colors), 16, 32)[0]).toMatchObject({ x: 16, y: 32 })
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

  it('draws rugs first and wall hangings before what stands', () => {
    const chair = { x: 0, y: 0, height: 5, pixels: dot('chair') }
    const rug = { x: 0, y: 10, height: 5, pixels: dot('rug'), layer: 'floor' as const }
    const mask = { x: 0, y: 0, height: 2, pixels: dot('mask'), layer: 'wall' as const }
    expect(scenePixels([chair, mask, rug]).map((p) => p.color)).toEqual(['rug', 'mask', 'chair'])
  })

  it('moves every pixel to where its item stands', () => {
    expect(scenePixels([{ x: 3, y: 4, height: 1, pixels: dot('a') }])[0]).toMatchObject({ x: 3, y: 4 })
  })
})
