import { CHARACTER_COLORS, CLOTH_COLORS, HAIR_COLORS, MOUTHS, SIGNAL_COLORS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone, type Theme } from './palette'
import { BODY, FACES, HAIR_STYLES, HANDS, HEAD, type Emotion, type HairStyle } from './sheets/character'
import { DESK } from './sheets/room'
import { SIGNALS, type Signal } from './sheets/signals'
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
  /** Shown above the head: an idea, a surprise, a question, sleep, done, trouble. */
  signal?: Signal
}

/**
 * Size of a desk with its agent, in pixels: room for a signal above the head, then the
 * agent, then the desk in front.
 */
export const STATION = { width: 32, height: 46, deskY: 25 } as const

/** Colours of a character: shared names, plus the tones of this appearance. */
export function characterColors(look: Appearance): Resolve {
  const [skinLight, skinMid, skinShadow] = SKIN_TONES[look.skin]
  const [hairBase, hairShade] = HAIR_COLORS[look.hair]
  const [clothLight, clothMid, clothDark] = CLOTH_COLORS[look.cloth]
  const colors: Record<string, string> = {
    ...CHARACTER_COLORS,
    mouth: MOUTHS[look.skin],
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

/**
 * Colours of a room sprite in a theme. The screen, its halo and the light it spills on
 * the desk follow the desk's state; with the screen off there is no halo and the desk
 * top keeps its wood.
 */
export function themeColors(theme: Theme, screen: ScreenState = 'off'): Resolve {
  return (name) => {
    if (name === 'screen') return theme[`screen.${screen}`]
    if (name === 'glow') return screen === 'off' ? null : theme[`glow.${screen}`]
    if (name === 'spill') return screen === 'off' ? theme['wood.light'] : theme[`spill.${screen}`]
    return (theme as Record<string, string>)[name] ?? (SIGNAL_COLORS as Record<string, string>)[name] ?? null
  }
}

/** An alert screen flashes: lit for two frames, dark for two. */
export const flashing = (screen: ScreenState, frame: number): ScreenState =>
  screen === 'alert' && frame % 4 >= 2 ? 'off' : screen

/**
 * A desk with its agent, as pixels in drawing order: body, head, face, hair behind
 * the desk, then the desk, then the hands resting on it.
 */
export function stationPixels(look: Appearance, pose: StationPose, theme: Theme): Pixel[] {
  const person = characterColors(look)
  const cx = 8
  const cy = STATION.deskY - 14
  const behind = [BODY, HEAD, FACES[pose.emotion], HAIR_STYLES[look.hairStyle]].flatMap((sprite) =>
    pixelsOf(sprite, 0, person, cx, cy),
  )
  const desk = pixelsOf(DESK, 0, themeColors(theme, pose.screen), 0, STATION.deskY)
  const hands = pixelsOf(HANDS[pose.hands], pose.frame, person, cx, cy)
  const signal = pose.signal ? pixelsOf(SIGNALS[pose.signal], 0, themeColors(theme), cx + 8, cy - 11) : []
  return [...behind, ...desk, ...hands, ...signal]
}
