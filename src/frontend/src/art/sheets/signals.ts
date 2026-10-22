// Generated once from verified drawings; from now on this file is edited by hand.
// Every string is one row of pixels; '.' is transparent. See docs/art-style.md.
import type { SpriteDef } from '../sprite'

const legend = {
  o: 'outline',
  Y: 'icon.yellow',
  y: 'icon.yellowShade',
  W: 'icon.white',
  g: 'icon.grey',
  A: 'icon.amber',
  B: 'icon.blue',
  K: 'icon.green',
  C: 'icon.cloud',
  c: 'icon.cloudShade',
}

const signal = (name: string, rows: string[]): SpriteDef => ({ name, width: 9, height: 10, legend, frames: [rows] })

/** Signals shown above an agent's head, told apart by shape before colour. */
export const SIGNALS = {
  bulb: signal('signal-bulb', [
    '...ooo...',
    '..oYYYo..',
    '.oYWYYYo.',
    '.oYWYYYo.',
    '.oYYYYyo.',
    '..oYyYo..',
    '...ogo...',
    '...ogo...',
    '...ooo...',
    '.........',
  ]),
  exclaim: signal('signal-exclaim', [
    '...ooo...',
    '...oAo...',
    '...oAo...',
    '...oAo...',
    '...oAo...',
    '...ooo...',
    '.........',
    '...ooo...',
    '...oAo...',
    '...ooo...',
  ]),
  question: signal('signal-question', [
    '..ooooo..',
    '.oBBBBBo.',
    '.oBoooBo.',
    '.oo..oBo.',
    '....oBo..',
    '...oBo...',
    '...ooo...',
    '...ooo...',
    '...oBo...',
    '...ooo...',
  ]),
  zz: signal('signal-zz', [
    '......ooo',
    '......oWo',
    '......ooo',
    'ooooooo..',
    'oWWWWWo..',
    'ooooWoo..',
    'oooWooo..',
    'ooWoooo..',
    'oWWWWWo..',
    'ooooooo..',
  ]),
  check: signal('signal-check', [
    '.........',
    '......ooo',
    '.....oKKo',
    'oo..oKKo.',
    'oKooKKo..',
    'oKKKKo...',
    '.oKKo....',
    '..oo.....',
    '.........',
    '.........',
  ]),
  storm: signal('signal-storm', [
    '..oooo...',
    '.oCCCCooo',
    'oCCCCCCCo',
    'oCcccccco',
    '.ooooooo.',
    '...oYo...',
    '..oYo....',
    '..oYYo...',
    '...oYo...',
    '...oo....',
  ]),
} as const

export type Signal = keyof typeof SIGNALS
