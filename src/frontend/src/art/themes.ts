import { BACK_ROOM_ARCADE_COLORS, COZY_EVENING_COLORS, NEON_DISTRICT_COLORS, SECURITY_FIRM_COLORS, type ThemeColors } from './palette'
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
}

export const THEMES = {
  cozy: {
    label: 'Cozy evening',
    colors: COZY_EVENING_COLORS,
    floor: FLOOR,
    wall: WALL,
    desk: DESK,
    furniture: ['rug', 'bookshelf', 'floorLamp', 'plant', 'sideTable', 'dragonLamp', 'whiteDog', 'keySword', 'blackCat', 'clubSofa'],
  },
  neonDistrict: {
    label: 'Neon district',
    colors: NEON_DISTRICT_COLORS,
    floor: NEON_DISTRICT_FLOOR,
    wall: NEON_DISTRICT_WALL,
    desk: NEON_DISTRICT_DESK,
    furniture: ['redBike', 'mechStatue', 'vendingMachine', 'serverRack', 'plant', 'starWalker'],
  },
  securityFirm: {
    label: 'Security firm',
    colors: SECURITY_FIRM_COLORS,
    floor: SECURITY_FIRM_FLOOR,
    wall: SECURITY_FIRM_WALL,
    desk: SECURITY_FIRM_DESK,
    furniture: ['whiteboard', 'corpPoster', 'waterCooler', 'plant', 'goldBars', 'serverRack', 'saveStar', 'throne'],
  },
  backRoomArcade: {
    label: 'Back-room arcade',
    colors: BACK_ROOM_ARCADE_COLORS,
    floor: BACK_ROOM_ARCADE_FLOOR,
    wall: BACK_ROOM_ARCADE_WALL,
    desk: BACK_ROOM_ARCADE_DESK,
    furniture: ['arcadeCabinet', 'itemPedestal', 'vendingMachine', 'serverRack', 'sideTable', 'whiteDog'],
  },
} satisfies Record<string, Theme>

export type ThemeName = keyof typeof THEMES
