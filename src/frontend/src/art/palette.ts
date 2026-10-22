/**
 * The colours of Openspace pixel art. Characters use the character palette, the same
 * in every office; rooms use a theme, chosen per office. See docs/art-style.md.
 */

export const CHARACTER_COLORS = {
  outline: '#2a1f2d',
  eye: '#1e1620',
  blush: '#e8907c',
  sweat: '#8fc6e8',
  'gear.light': '#e3e6ee',
  'gear.dark': '#9aa0ad',
} as const

/** Light, mid and shadow tones. */
export const SKIN_TONES = {
  fair: ['#f6d5bc', '#e8b694', '#c98f72'],
  medium: ['#e2a77e', '#c6885f', '#9e6646'],
  tan: ['#b97c54', '#985f3f', '#714431'],
  deep: ['#8e5c40', '#6c4430', '#523424'],
} as const

/**
 * The mouth colour of each skin tone. Mouths and eyes must stand out from the skin by
 * lightness, not only hue (at least 3:1), so they read for colour-blind people too:
 * dark mouths on light skin, a light mouth on the deepest tone. Tests enforce it.
 */
export const MOUTHS: Record<keyof typeof SKIN_TONES, string> = {
  fair: '#9c4a46',
  medium: '#7a3230',
  tan: '#3e1c1e',
  deep: '#f4bfae',
}

/** Colours of the signals shown above heads; the same in every office. */
export const SIGNAL_COLORS = {
  'icon.yellow': '#f6d35a',
  'icon.yellowShade': '#c9a23a',
  'icon.white': '#fff8ec',
  'icon.grey': '#8e8f9c',
  'icon.amber': '#f2b84b',
  'icon.blue': '#8fc6e8',
  'icon.green': '#8fd282',
  'icon.cloud': '#c3c6d1',
  'icon.cloudShade': '#8f92a0',
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
  'glow.on',
  'glow.alert',
  'spill.on',
  'spill.alert',
  'key.light',
  'key.dark',
  'leaf.light',
  'leaf.mid',
  'leaf.dark',
  'pot.light',
  'pot.dark',
  'mug.light',
  'mug.dark',
  'steam',
  'board.light',
  'board.shade',
  'ink.blue',
  'ink.red',
  'note.yellow',
  'note.yellowShade',
  'note.green',
  'note.greenShade',
  'led.on',
  'led.off',
  'led.alert',
  'lamp.shade',
  'lamp.shadeDark',
  'lamp.light',
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
  'glow.on': '#d4fff0',
  'glow.alert': '#ffe2a3',
  'spill.on': '#b4b88c',
  'spill.alert': '#e3a75a',
  'key.light': '#d9d4cc',
  'key.dark': '#a8a29a',
  'leaf.light': '#8fc46a',
  'leaf.mid': '#5f9a4c',
  'leaf.dark': '#3d6b3a',
  'pot.light': '#c56e4a',
  'pot.dark': '#94503a',
  'mug.light': '#e8e2d6',
  'mug.dark': '#b9b1a3',
  steam: '#d7d2cc',
  'board.light': '#ecebe4',
  'board.shade': '#cfcdc4',
  'ink.blue': '#4a6fb3',
  'ink.red': '#c2544c',
  'note.yellow': '#f3d66b',
  'note.yellowShade': '#c9ab45',
  'note.green': '#a6db8c',
  'note.greenShade': '#79b062',
  'led.on': '#7fe08f',
  'led.off': '#2f4a36',
  'led.alert': '#f2b84b',
  'lamp.shade': '#f0c27a',
  'lamp.shadeDark': '#c9944f',
  'lamp.light': '#fff1c9',
}

/** A gloomy mood: cold greys and blue screens, for the office that never sleeps. */
export const LATE_NIGHT: Theme = {
  outline: '#14141c',
  'floor.light': '#3b4250',
  'floor.mid': '#323845',
  'floor.dark': '#252a35',
  'wall.top': '#4a5160',
  'wall.light': '#3a404d',
  'wall.mid': '#323743',
  'wall.shadow': '#262a34',
  'trim.light': '#4d5462',
  'trim.dark': '#353a46',
  'wood.light': '#6d6a72',
  'wood.mid': '#56535b',
  'wood.dark': '#403e45',
  'metal.light': '#5d6372',
  'metal.mid': '#434855',
  'metal.dark': '#2d313b',
  'screen.off': '#1d2028',
  'screen.on': '#9ec9ff',
  'screen.alert': '#ff8a65',
  'glow.on': '#dcecff',
  'glow.alert': '#ffd2c2',
  'spill.on': '#7d8ba3',
  'spill.alert': '#a77a6e',
  'key.light': '#b8bcc6',
  'key.dark': '#8a8f9a',
  'leaf.light': '#7aa38a',
  'leaf.mid': '#557d68',
  'leaf.dark': '#3a5a4b',
  'pot.light': '#7d8494',
  'pot.dark': '#5b6170',
  'mug.light': '#d6d9e0',
  'mug.dark': '#a5aab5',
  steam: '#c3c7d0',
  'board.light': '#d9dce3',
  'board.shade': '#b7bbc5',
  'ink.blue': '#3d5a99',
  'ink.red': '#a8434b',
  'note.yellow': '#d9c672',
  'note.yellowShade': '#ad9c50',
  'note.green': '#8fbf9b',
  'note.greenShade': '#6a9978',
  'led.on': '#9ec9ff',
  'led.off': '#26303f',
  'led.alert': '#ff8a65',
  'lamp.shade': '#c9cfdb',
  'lamp.shadeDark': '#9aa1b0',
  'lamp.light': '#f2f6ff',
}

/** A futuristic mood: dark violet, cyan screens and pink neon trims. */
export const NEON: Theme = {
  outline: '#140c24',
  'floor.light': '#2e2350',
  'floor.mid': '#261c44',
  'floor.dark': '#1b1333',
  'wall.top': '#46307a',
  'wall.light': '#35255e',
  'wall.mid': '#2c1f50',
  'wall.shadow': '#201640',
  'trim.light': '#ff4fa3',
  'trim.dark': '#a8306b',
  'wood.light': '#4b3f6e',
  'wood.mid': '#3a3058',
  'wood.dark': '#2a2242',
  'metal.light': '#6a6290',
  'metal.mid': '#4a4370',
  'metal.dark': '#2f2a4a',
  'screen.off': '#1a1530',
  'screen.on': '#3ff0e0',
  'screen.alert': '#ff4fa3',
  'glow.on': '#c6fff9',
  'glow.alert': '#ffc2e0',
  'spill.on': '#3fa7a6',
  'spill.alert': '#b04a86',
  'key.light': '#c2bdf0',
  'key.dark': '#8f88c4',
  'leaf.light': '#5fe0b0',
  'leaf.mid': '#36b08a',
  'leaf.dark': '#227a62',
  'pot.light': '#7a6ad0',
  'pot.dark': '#5446a0',
  'mug.light': '#e9e4ff',
  'mug.dark': '#b6aee6',
  steam: '#cfc8f5',
  'board.light': '#e6e2ff',
  'board.shade': '#c2bce6',
  'ink.blue': '#3d4bd9',
  'ink.red': '#e0307f',
  'note.yellow': '#ffe066',
  'note.yellowShade': '#d1b23f',
  'note.green': '#5fe0b0',
  'note.greenShade': '#36b08a',
  'led.on': '#3ff0e0',
  'led.off': '#2a2552',
  'led.alert': '#ff4fa3',
  'lamp.shade': '#ff4fa3',
  'lamp.shadeDark': '#c43581',
  'lamp.light': '#ffd6ec',
}

export const THEMES = { cozy: COZY_EVENING, lateNight: LATE_NIGHT, neon: NEON } as const
export type ThemeName = keyof typeof THEMES
