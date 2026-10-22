import { CHARACTER_COLORS, CLOTH_COLORS, HAIR_COLORS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone, type Theme } from './palette'
import { BODY, FACES, HAIR_STYLES, HANDS, HEAD, type Emotion, type HairStyle } from './sheets/character'
import { DESK } from './sheets/room'
import { pixelsOf, type Pixel, type Resolve } from './sprite'

/** How the agent at a desk looks. */
export interface Appearance {
  skin: SkinTone
  hair: HairColor
  hairStyle: HairStyle
  cloth: ClothColor
}

export type ScreenState = 'off' | 'on' | 'alert'

export interface StationPose {
  emotion: Emotion | 'blink'
  hands: 'rest' | 'typing'
  frame: number
  screen: ScreenState
}

/** Size of a desk with its agent, in pixels, and where the desk sits in it. */
export const STATION = { width: 32, height: 35, deskY: 14 } as const

/** Colours of a character: shared names, plus the tones of this appearance. */
export function characterColors(look: Appearance): Resolve {
  const [skinLight, skinMid, skinShadow] = SKIN_TONES[look.skin]
  const [hairBase, hairShade] = HAIR_COLORS[look.hair]
  const [clothLight, clothMid, clothDark] = CLOTH_COLORS[look.cloth]
  const colors: Record<string, string> = {
    ...CHARACTER_COLORS,
    'skin.light': skinLight,
    'skin.mid': skinMid,
    'skin.shadow': skinShadow,
    'hair.base': hairBase,
    'hair.shade': hairShade,
    'cloth.light': clothLight,
    'cloth.mid': clothMid,
    'cloth.dark': clothDark,
  }
  return (name) => colors[name] ?? null
}

/** Colours of a room sprite in a theme; the screen follows the desk's state. */
export function themeColors(theme: Theme, screen: ScreenState = 'off'): Resolve {
  return (name) => (name === 'screen' ? theme[`screen.${screen}`] : ((theme as Record<string, string>)[name] ?? null))
}

/**
 * A desk with its agent, as pixels in drawing order: body, head, face, hair behind
 * the desk, then the desk, then the hands resting on it.
 */
export function stationPixels(look: Appearance, pose: StationPose, theme: Theme): Pixel[] {
  const person = characterColors(look)
  const cx = 8
  const cy = STATION.deskY + 10 - 24
  const behind = [BODY, HEAD, FACES[pose.emotion], HAIR_STYLES[look.hairStyle]].flatMap((sprite) =>
    pixelsOf(sprite, 0, person, cx, cy),
  )
  const desk = pixelsOf(DESK, 0, themeColors(theme, pose.screen), 0, STATION.deskY)
  const hands = pixelsOf(HANDS[pose.hands], pose.frame, person, cx, cy)
  return [...behind, ...desk, ...hands]
}
