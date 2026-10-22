import { BONE, CHARACTER_COLORS, GLOVES, CLOTH_COLORS, HAIR_COLORS, MOUTHS, OBJECT_COLORS, SIGNAL_COLORS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone, type ThemeColors } from './palette'
import { FAN_COLORS, FAN_COSTUMES } from './fanPack'
import type { Light } from './lighting'
import { ACCESSORIES, BODY, FACES, GESTURES, HAIR_STYLES, HANDS, HEAD, COSTUMES, OUTFITS, type Accessory, type Costume, type Emotion, type HairStyle, type Outfit } from './sheets/character'
import { SIGNALS, type Signal } from './sheets/signals'
import { pixelsOf, type Pixel, type Resolve, type SpriteDef } from './sprite'
import type { Theme } from './themes'

/** How the agent at a desk looks. */
export interface Appearance {
  skin: SkinTone
  hair: HairColor
  hairStyle: HairStyle
  cloth: ClothColor
  /** Glasses, a headset…: usually given by the role. */
  accessory?: Accessory
  /** A hoodie or a long coat instead of the plain shirt. */
  outfit?: Outfit
  /** Replaces head, hair and clothes: for the special agents. */
  costume?: Costume
}

export type Gesture = 'rest' | 'typing' | 'think' | 'wave' | 'cheer' | 'stuck'

/**
 * What the agent does with the arms: raised arms are drawn behind the desk, hands
 * resting on it in front.
 */
export const GESTURE_PARTS: Record<Gesture, { arms?: SpriteDef; hands?: SpriteDef }> = {
  rest: { hands: HANDS.rest },
  typing: { hands: HANDS.typing },
  think: { arms: GESTURES.think, hands: HANDS.left },
  wave: { arms: GESTURES.wave, hands: HANDS.left },
  cheer: { arms: GESTURES.cheer },
  stuck: { arms: GESTURES.stuck },
}

export type ScreenState = 'off' | 'on' | 'alert'

export interface StationPose {
  emotion: Emotion | 'blink'
  gesture: Gesture
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
  // Costumes have bones or gloves where skin would show.
  const [skinLight, skinMid, skinShadow] = look.costume === 'skeleton' ? BONE : look.costume === 'hooded' ? GLOVES : SKIN_TONES[look.skin]
  const [hairBase, hairShade] = HAIR_COLORS[look.hair]
  // The skeleton always wears its blue hoodie.
  const [clothLight, clothMid, clothDark] = CLOTH_COLORS[look.costume === 'skeleton' ? 'blue' : look.cloth]
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
  return (name) => colors[name] ?? FAN_COLORS[name] ?? null
}

/**
 * Colours of a room sprite in a theme. The screen, its halo and the light it spills on
 * the desk follow the desk's state; with the screen off there is no halo and the desk
 * top keeps its wood.
 */
export function themeColors(theme: ThemeColors, screen: ScreenState = 'off'): Resolve {
  return (name) => {
    if (name === 'screen') return theme[`screen.${screen}`]
    if (name === 'glow') return screen === 'off' ? null : theme[`glow.${screen}`]
    if (name === 'spill') return screen === 'off' ? theme['wood.light'] : theme[`spill.${screen}`]
    return (theme as Record<string, string>)[name] ?? OBJECT_COLORS[name] ?? (SIGNAL_COLORS as Record<string, string>)[name] ?? FAN_COLORS[name] ?? null
  }
}

/** An alert screen flashes: lit for two frames, dark for two. */
export const flashing = (screen: ScreenState, frame: number): ScreenState =>
  screen === 'alert' && frame % 4 >= 2 ? 'off' : screen

/**
 * A desk with its agent, as pixels in drawing order: body, head, face, hair, accessory
 * and raised arms behind the desk, then the desk, then the hands resting on it.
 */
export function stationPixels(look: Appearance, pose: StationPose, theme: Theme): Pixel[] {
  const person = characterColors(look)
  const cx = 8
  const cy = STATION.deskY - 14
  const { arms, hands } = GESTURE_PARTS[pose.gesture]
  const closed = pose.emotion === 'blink' || pose.emotion === 'asleep'
  const costume = look.costume && (FAN_COSTUMES?.[look.costume] ?? COSTUMES[look.costume])
  const self = costume
    ? [costume.body, closed ? costume.closed : costume.open]
    : [look.outfit ? OUTFITS[look.outfit] : BODY, HEAD, FACES[pose.emotion], HAIR_STYLES[look.hairStyle]]
  const layers = [...self, look.accessory && ACCESSORIES[look.accessory], arms]
  const behind = layers.flatMap((sprite) => (sprite ? pixelsOf(sprite, pose.frame, person, cx, cy) : []))
  const desk = pixelsOf(theme.desk, 0, themeColors(theme.colors, pose.screen), 0, STATION.deskY)
  const front = hands ? pixelsOf(hands, pose.frame, person, cx, cy) : []
  const signal = pose.signal ? pixelsOf(SIGNALS[pose.signal], 0, themeColors(theme.colors), cx + 8, cy - 11) : []
  return [...behind, ...desk, ...front, ...signal]
}

/** The light a desk's screen casts while it is on, in the station's own pixels. */
export function stationLights(pose: StationPose, theme: Theme): Light[] {
  if (pose.screen === 'off') return []
  return [{ x: 12, y: STATION.deskY + 3, radius: 26, color: theme.colors[`screen.${pose.screen}`] }]
}
