import {
  BACK_ROOM_ARCADE_COLORS,
  BRIGHT_STUDIO_COLORS,
  COZY_EVENING_COLORS,
  DARK_FOCUS_COLORS,
  NEON_DISTRICT_COLORS,
  SECURITY_FIRM_COLORS,
  TOWN_HALL_COLORS,
  type ThemeColors,
} from './palette'
import type { PropName } from './sheets/props'
import { DESK, FLOOR, WALL } from './sheets/room'
import {
  BACK_ROOM_ARCADE_DESK,
  BACK_ROOM_ARCADE_FLOOR,
  BACK_ROOM_ARCADE_WALL,
  NEON_DISTRICT_DESK,
  NEON_DISTRICT_FLOOR,
  NEON_DISTRICT_WALL,
  SECURITY_FIRM_DESK,
  SECURITY_FIRM_FLOOR,
  SECURITY_FIRM_WALL,
} from './sheets/rooms'
import type { SpriteDef } from './sprite'

/**
 * An office mood: its own floor, walls and desk model, the colours they are painted
 * with, and the furniture that suits it best. Any prop still fits in any theme.
 */
export interface Theme {
  label: string
  colors: ThemeColors
  floor: SpriteDef
  wall: SpriteDef
  desk: SpriteDef
  furniture: PropName[]
  /** What stands in the corridors, in turn. */
  hall: PropName[]
  /** Always in the dark, whatever the hour: only screens and lamps light it. */
  dark?: boolean
}

export const THEMES = {
  cozy: {
    label: 'Cozy evening',
    colors: COZY_EVENING_COLORS,
    floor: FLOOR,
    wall: WALL,
    desk: DESK,
    furniture: ['rug', 'bookshelf', 'floorLamp', 'plant', 'sideTable', 'dragonLamp', 'whiteDog', 'keySword', 'blackCat', 'clubSofa'],
    hall: ['plant', 'waterCooler', 'bookshelf', 'sideTable'],
  },
  neonDistrict: {
    label: 'Neon district',
    colors: NEON_DISTRICT_COLORS,
    floor: NEON_DISTRICT_FLOOR,
    wall: NEON_DISTRICT_WALL,
    desk: NEON_DISTRICT_DESK,
    furniture: ['redBike', 'mechStatue', 'vendingMachine', 'serverRack', 'plant', 'starWalker'],
    hall: ['vendingMachine', 'plant', 'serverRack'],
  },
  securityFirm: {
    label: 'Security firm',
    colors: SECURITY_FIRM_COLORS,
    floor: SECURITY_FIRM_FLOOR,
    wall: SECURITY_FIRM_WALL,
    desk: SECURITY_FIRM_DESK,
    furniture: ['whiteboard', 'corpPoster', 'waterCooler', 'plant', 'goldBars', 'serverRack', 'saveStar', 'throne'],
    hall: ['waterCooler', 'plant', 'vendingMachine'],
  },
  backRoomArcade: {
    label: 'Back-room arcade',
    colors: BACK_ROOM_ARCADE_COLORS,
    floor: BACK_ROOM_ARCADE_FLOOR,
    wall: BACK_ROOM_ARCADE_WALL,
    desk: BACK_ROOM_ARCADE_DESK,
    furniture: ['arcadeCabinet', 'itemPedestal', 'vendingMachine', 'serverRack', 'sideTable', 'whiteDog'],
    hall: ['arcadeCabinet', 'vendingMachine', 'plant'],
  },
  brightStudio: {
    label: 'Bright studio',
    colors: BRIGHT_STUDIO_COLORS,
    floor: FLOOR,
    wall: WALL,
    desk: DESK,
    furniture: ['plant', 'rug', 'bookshelf', 'floorLamp', 'sideTable', 'whiteDog'],
    hall: ['plant', 'waterCooler', 'sideTable'],
  },
  darkFocus: {
    label: 'Dark focus',
    colors: DARK_FOCUS_COLORS,
    floor: NEON_DISTRICT_FLOOR,
    wall: NEON_DISTRICT_WALL,
    desk: NEON_DISTRICT_DESK,
    furniture: ['serverRack', 'vendingMachine', 'plant'],
    hall: ['serverRack', 'vendingMachine'],
    dark: true,
  },
  townHall: {
    label: 'Town hall',
    colors: TOWN_HALL_COLORS,
    floor: SECURITY_FIRM_FLOOR,
    wall: SECURITY_FIRM_WALL,
    desk: SECURITY_FIRM_DESK,
    furniture: ['waterCooler', 'whiteboard', 'plant'],
    hall: ['waterCooler', 'plant'],
  },
} satisfies Record<string, Theme>

export type ThemeName = keyof typeof THEMES

/** The theme an office asks for, or the default when this version does not know it. */
export const themeOf = (name: string): Theme => (name in THEMES ? THEMES[name as ThemeName] : THEMES.cozy)
