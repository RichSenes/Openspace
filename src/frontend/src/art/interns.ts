import { CHARACTER_LEGEND } from './sheets/character'
import { SIGNAL_COLORS } from './palette'
import type { SpriteDef } from './sprite'
import { STATION } from './station'

/**
 * An intern: a helper agent standing beside its agent's desk. Smaller than an agent and
 * on its feet, with a visitor badge on a lanyard and a clipboard, so it never reads as
 * another desk. It wears its agent's colours.
 */
export const INTERN: SpriteDef = {
  name: 'intern',
  width: 12,
  height: 22,
  legend: { ...CHARACTER_LEGEND, j: 'clip.board', l: 'clip.paper' },
  frames: [
    [
      '...oooooo...',
      '..oHHHHHHo..',
      '.oHHHHHHHHo.',
      '.oHSSSSSSHo.',
      '.oSSeSSeSSo.',
      '.oSSSSSSSSo.',
      '..oSSmmSSo..',
      '...oSSSSo...',
      '..oCwzzwCo..',
      '.oCCwCCwCCo.',
      '.oCCCwwCCCo.',
      'ojkjoCkkCCo.',
      'ojlljCkkCCo.',
      'ojlljCCCCCo.',
      'ojlljCCCCSo.',
      'ojjjjoqqqqo.',
      '..onnnnnno..',
      '..onnnnnno..',
      '..onnoonno..',
      '..onnoonno..',
      '.oxxxooxxxo.',
      '.oooo..oooo.',
    ],
    // Breathing: the clipboard and the badge rise by a pixel.
    [
      '...oooooo...',
      '..oHHHHHHo..',
      '.oHHHHHHHHo.',
      '.oHSSSSSSHo.',
      '.oSSeSSeSSo.',
      '.oSSSSSSSSo.',
      '..oSSmmSSo..',
      '...oSSSSo...',
      '..oCwzzwCo..',
      '.oCCwCCwCCo.',
      'ojkjowwCCCo.',
      'ojlljkkCCCo.',
      'ojlljkkCCCo.',
      'ojlljCCCCSo.',
      'ojjjjCCCCCo.',
      '.ooooqqqqqo.',
      '..onnnnnno..',
      '..onnnnnno..',
      '..onnoonno..',
      '..onnoonno..',
      '.oxxxooxxxo.',
      '.oooo..oooo.',
    ],
  ],
}

/** Colours of the clipboard, the same for every intern. */
export const CLIPBOARD_COLORS = { 'clip.board': '#a8743f', 'clip.paper': '#f4efe4' } as const

/**
 * The puff of smoke an intern arrives and leaves in, a few sparks flying: four frames,
 * from a small cloud to a big one breaking up.
 */
export const PUFF: SpriteDef = {
  name: 'puff',
  width: 18,
  height: 16,
  legend: { o: 'smoke.dark', c: 'smoke.light', y: 'icon.yellow', w: 'icon.white' },
  frames: [
    [
      '..................',
      '..................',
      '..................',
      '..................',
      '..................',
      '..................',
      '........y.........',
      '.......occo.......',
      '......occcco......',
      '.....occcccco.....',
      '.....occcccco.....',
      '......occcco......',
      '.......oooo.......',
      '..................',
      '..................',
      '..................',
    ],
    [
      '..................',
      '..................',
      '....y.......w.....',
      '..................',
      '......oooo........',
      '.....occcco.oooo..',
      '....occcccooccco..',
      '...occcccccccccco.',
      '..occcccccccccco..',
      '..occcccccccccco..',
      '...occcccccccco...',
      '..occccccccccco...',
      '...ooccccccooo....',
      '.....oooooo.......',
      '..................',
      '..................',
    ],
    [
      '..w...........y...',
      '..................',
      '.....oooo..oooo...',
      '....occccooccccoo.',
      '...occcccccccccco.',
      '.ooccccccccccccco.',
      'occcccccccccccco..',
      'occcccccccccccccoo',
      '.occcccccccccccccc',
      '..occcccccccccccco',
      '.occcccccccccccco.',
      'occccccccccccccco.',
      '.ooccccccccccccoo.',
      '...ooccccccccoo...',
      '.....oooooooo.....',
      '..................',
    ],
    [
      'y................w',
      '..................',
      '..ooo.........ooo.',
      '.occco.......occco',
      '.occco..ooo..occco',
      '..ooo..occco..ooo.',
      '.......occco......',
      '..oo....ooo.....oo',
      '.occo..........occ',
      '.occo...........oo',
      '..oo....oooo......',
      '.......occcco.....',
      '..oo...occcco..oo.',
      '.occo...oooo..occo',
      '..oo...........oo.',
      '..................',
    ],
  ],
}

export const SMOKE_COLORS = { 'smoke.light': '#e4e2ea', 'smoke.dark': '#a9a6b6' } as const

/** Where interns stand, beside their desk: right of it, then left, never more than two. */
export const INTERN_SPOTS = [
  { dx: STATION.width - 4, dy: STATION.height - 22 + 1 },
  { dx: -8, dy: STATION.height - 22 + 1 },
] as const

export const PUFF_COLORS = { ...SMOKE_COLORS, 'icon.yellow': SIGNAL_COLORS['icon.yellow'], 'icon.white': SIGNAL_COLORS['icon.white'] }
