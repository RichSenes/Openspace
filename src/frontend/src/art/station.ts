import { BONE, CHARACTER_COLORS, DOLL_LIPS, PORCELAIN, GLOVES, CLOTH_COLORS, HAIR_COLORS, MOUTHS, OBJECT_COLORS, SIGNAL_COLORS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone, type ThemeColors } from './palette'
import { batteryPixels } from './energy'
import { FAN_COLORS, FAN_COSTUMES } from './fanPack'
import type { Light } from './lighting'
import { ACCESSORIES, BODY, CHARACTER_LEGEND, FACES, GESTURES, HAIR_STYLES, HANDS, HEAD, COSTUMES, OUTFITS, WINDUP_KEY, type Accessory, type Costume, type Emotion, type HairStyle, type Outfit } from './sheets/character'
import { SIGNALS, type Signal } from './sheets/signals'
import { pixelsOf, type Pixel, type Resolve, type SpriteDef } from './sprite'
import type { Theme } from './themes'
import { GUEST_DESK } from './sheets/room'

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
  /** A guest from outside Openspace: a visitor's badge, and a laptop instead of a monitor. */
  visitor?: boolean
}

export type Gesture = 'rest' | 'typing' | 'think' | 'wave' | 'cheer' | 'stuck' | 'sip'

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
  sip: { arms: GESTURES.sip, hands: HANDS.left },
}

export type ScreenState = 'off' | 'on' | 'alert'

export interface StationPose {
  emotion: Emotion | 'blink'
  gesture: Gesture
  frame: number
  screen: ScreenState
  /** Shown above the head: an idea, a surprise, a question, sleep, done, trouble. */
  signal?: Signal
  /** The energy shown on the battery beside the agent, from 0 to 1; no battery when unknown. */
  energy?: number
  /** Pixels the agent sinks into its chair when exhausted. */
  slump?: number
  /** Gone for a walk: only the desk is drawn, its screen as the work left it. */
  away?: boolean
}

/**
 * Size of a desk with its agent, in pixels: room for a signal above the head, then the
 * agent, then the desk in front.
 */
export const STATION = { width: 32, height: 46, deskY: 25 } as const

/** Colours of a character: shared names, plus the tones of this appearance. */
export function characterColors(look: Appearance): Resolve {
  // Costumes have bones or gloves where skin would show.
  const doll = look.costume === 'karakuri'
  const [skinLight, skinMid, skinShadow] =
    look.costume === 'skeleton' ? BONE : look.costume === 'hooded' ? GLOVES : doll ? PORCELAIN : SKIN_TONES[look.skin]
  // The karakuri always has black hair and a red kimono; the skeleton its blue hoodie.
  const [hairBase, hairShade] = HAIR_COLORS[doll ? 'black' : look.hair]
  const [clothLight, clothMid, clothDark] = CLOTH_COLORS[look.costume === 'skeleton' ? 'blue' : doll ? 'red' : look.cloth]
  const colors: Record<string, string> = {
    ...CHARACTER_COLORS,
    mouth: doll ? DOLL_LIPS : MOUTHS[look.skin],
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
    : [look.outfit ? OUTFITS[look.outfit] : BODY, look.visitor ? VISITOR_BADGE : undefined, HEAD, FACES[pose.emotion], HAIR_STYLES[look.hairStyle]]
  const doll = look.costume === 'karakuri'
  // A karakuri's key turns while it works, and stands still while it sleeps.
  const key = doll ? pixelsOf(WINDUP_KEY, pose.screen === 'on' ? Math.floor(pose.frame / 2) % 2 : 0, person, cx, cy) : []
  // An exhausted agent sinks into its chair; its arms still reach the desk.
  const sunk = cy + (pose.slump ?? 0)
  const layers = [...self, look.accessory && ACCESSORIES[look.accessory]]
  const behind = [
    ...key,
    ...layers.flatMap((sprite) => (sprite ? pixelsOf(sprite, pose.frame, person, cx, sunk) : [])),
    ...(arms ? pixelsOf(arms, pose.frame, person, cx, cy) : []),
  ]
  const desk = [
    ...pixelsOf(look.visitor ? GUEST_DESK : theme.desk, 0, themeColors(theme.colors, pose.screen), 0, STATION.deskY),
    ...(doll ? pixelsOf(HOURGLASS, pose.screen === 'on' ? Math.floor(pose.frame / 3) % 2 : 0, themeColors(theme.colors), 26, STATION.deskY + 7) : []),
  ]
  if (pose.away) return desk
  const front = hands ? pixelsOf(hands, pose.frame, person, cx, cy) : []
  const signal = pose.signal ? pixelsOf(SIGNALS[pose.signal], 0, themeColors(theme.colors), cx + 8, cy - 11) : []
  const battery = pose.energy === undefined ? [] : batteryPixels(pose.energy)
  return [...behind, ...desk, ...front, ...signal, ...battery]
}

/** A visitor's badge on a lanyard, hanging on the chest of a guest. */
export const VISITOR_BADGE: SpriteDef = {
  name: 'visitor-badge',
  width: 24,
  height: 32,
  legend: CHARACTER_LEGEND,
  frames: [
    [
      ...Array<string>(17).fill('........................'),
      '.........w....w.........',
      '.........w....w.........',
      '..........w..w..........',
      '..........oooo..........',
      '..........oYYo..........',
      '..........okko..........',
      '..........oooo..........',
      ...Array<string>(8).fill('........................'),
    ],
  ],
}

/** The hourglass on a routine's desk: sand runs down while it works. */
export const HOURGLASS: SpriteDef = {
  name: 'hourglass',
  width: 5,
  height: 8,
  legend: { o: 'outline', w: 'plinth.mid', g: 'board.light', s: 'note.yellow' },
  frames: [
    ['owwwo', '.oso.', '.oso.', '..o..', '.ogo.', '.oso.', 'owwwo', '.....'],
    ['owwwo', '.ogo.', '.oso.', '..s..', '.oso.', '.oso.', 'owwwo', '.....'],
  ],
}

/** The light a desk's screen casts while it is on, in the station's own pixels. */
export function stationLights(pose: StationPose, theme: Theme): Light[] {
  if (pose.screen === 'off') return []
  return [{ x: 12, y: STATION.deskY + 3, radius: 26, color: theme.colors[`screen.${pose.screen}`] }]
}
