/**
 * The colours of Openspace pixel art. Characters use the character palette, the same
 * in every office; rooms use a theme, chosen per office. See docs/art-style.md.
 */

export const CHARACTER_COLORS = {
  outline: '#2a1f2d',
  eye: '#2a1f2d',
  mouth: '#9c4a46',
  blush: '#e8907c',
  sweat: '#8fc6e8',
} as const

/** Light, mid and shadow tones. */
export const SKIN_TONES = {
  fair: ['#f6d5bc', '#e8b694', '#c98f72'],
  medium: ['#e2a77e', '#c6885f', '#9e6646'],
  tan: ['#b97c54', '#985f3f', '#714431'],
  deep: ['#7d5038', '#603c29', '#45291d'],
} as const

/** Base and shade. */
export const HAIR_COLORS = {
  black: ['#3a3240', '#241e2a'],
  brown: ['#6e4632', '#4c3022'],
  auburn: ['#a24a30', '#743320'],
  blonde: ['#e2b65e', '#b88a3c'],
  grey: ['#b9b4ae', '#8b8681'],
} as const

/** Light, mid and dark. */
export const CLOTH_COLORS = {
  blue: ['#5b86c4', '#41679f', '#2d4a75'],
  green: ['#64a374', '#487d57', '#30573c'],
  red: ['#c45a52', '#9a423d', '#6e2c2a'],
  purple: ['#8a66bd', '#6a4c97', '#4b346d'],
  grey: ['#959aa4', '#727782', '#525660'],
  mustard: ['#dcab42', '#ae842c', '#7e5e1e'],
} as const

export type SkinTone = keyof typeof SKIN_TONES
export type HairColor = keyof typeof HAIR_COLORS
export type ClothColor = keyof typeof CLOTH_COLORS

/** Every name a room sprite may use. A theme gives each one a colour. */
export const THEME_KEYS = [
  'outline',
  'floor.light',
  'floor.mid',
  'floor.dark',
  'wall.top',
  'wall.light',
  'wall.mid',
  'wall.shadow',
  'trim.light',
  'trim.dark',
  'wood.light',
  'wood.mid',
  'wood.dark',
  'metal.light',
  'metal.mid',
  'metal.dark',
  'screen.off',
  'screen.on',
  'screen.alert',
  'key.light',
  'key.dark',
] as const

export type ThemeKey = (typeof THEME_KEYS)[number]
export type Theme = Record<ThemeKey, string>

/** The default mood: warm wood, muted blue-grey walls, amber lights. */
export const COZY_EVENING: Theme = {
  outline: '#2a1f2d',
  'floor.light': '#8c5d3f',
  'floor.mid': '#784e33',
  'floor.dark': '#5c3a26',
  'wall.top': '#6a6982',
  'wall.light': '#56556c',
  'wall.mid': '#4a495e',
  'wall.shadow': '#3a394b',
  'trim.light': '#7a5a44',
  'trim.dark': '#55402f',
  'wood.light': '#c08a5c',
  'wood.mid': '#9c6b44',
  'wood.dark': '#74492d',
  'metal.light': '#6c6f7d',
  'metal.mid': '#4c4f5c',
  'metal.dark': '#34363f',
  'screen.off': '#2b2e38',
  'screen.on': '#8fe0c4',
  'screen.alert': '#f2b84b',
  'key.light': '#d9d4cc',
  'key.dark': '#a8a29a',
}

export const THEMES = { cozy: COZY_EVENING } as const
export type ThemeName = keyof typeof THEMES
