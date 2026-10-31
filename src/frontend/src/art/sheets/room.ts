// Generated once from verified drawings; from now on this file is edited by hand.
// Every string is one row of pixels; '.' is transparent. See docs/art-style.md.
import type { SpriteDef } from '../sprite'

export const FLOOR: SpriteDef = {
  name: 'floor',
  width: 16,
  height: 16,
  legend: { L: 'floor.light', M: 'floor.mid', D: 'floor.dark' },
  frames: [
    [
      'LLLLLLLLLLLDLLLL',
      'MMMMMMMMMMMDMMMM',
      'MMMMMMMMMMMDMMMM',
      'DDDDDDDDDDDDDDDD',
      'LLLLDLLLLLLLLLLL',
      'MMMMDMMMMMMMMMMM',
      'MMMMDMMMMMMMMMMM',
      'DDDDDDDDDDDDDDDD',
      'LLLLLLLLLLLLLDLL',
      'MMMMMMMMMMMMMDMM',
      'MMMMMMMMMMMMMDMM',
      'DDDDDDDDDDDDDDDD',
      'LLLLLLLDLLLLLLLL',
      'MMMMMMMDMMMMMMMM',
      'MMMMMMMDMMMMMMMM',
      'DDDDDDDDDDDDDDDD',
    ],
  ],
}

export const WALL: SpriteDef = {
  name: 'wall',
  width: 16,
  height: 16,
  legend: { t: 'wall.top', s: 'wall.shadow', l: 'wall.light', m: 'wall.mid', r: 'trim.light', d: 'trim.dark' },
  frames: [
    [
      'tttttttttttttttt',
      'ssssssssssssssss',
      'lllllllmllllllll',
      'lllllllmllllllll',
      'lllllllmllllllll',
      'lllllllmllllllll',
      'lllllllmllllllll',
      'lllllllmllllllll',
      'lllllllmllllllll',
      'lllllllmllllllll',
      'lllllllmllllllll',
      'mmmmmmmmmmmmmmmm',
      'ssssssssssssssss',
      'rrrrrrrrrrrrrrrr',
      'rrrrrrrrrrrrrrrr',
      'dddddddddddddddd',
    ],
  ],
}

/**
 * The desk with its monitor, back to the viewer. 'g' is the lit edge of the screen,
 * 'G' its halo and 'P' the light it spills on the desk top; all follow the desk's state.
 */
export const DESK: SpriteDef = {
  name: 'desk',
  width: 32,
  height: 21,
  legend: {
    o: 'outline',
    j: 'metal.light',
    k: 'metal.mid',
    K: 'metal.dark',
    T: 'wood.light',
    t: 'wood.mid',
    F: 'wood.mid',
    f: 'wood.dark',
    a: 'key.light',
    A: 'key.dark',
    g: 'screen',
    G: 'glow',
    P: 'spill',
  },
  frames: [
    [
      '..oooooooooo....................',
      '..ojjjjjjjkog...................',
      '..ojkkkkkkkogG..................',
      '..ojkkkkkkkogG..................',
      '..ojkkkkkkkogG..................',
      '..ojkkkkkkkogG..................',
      '..okkkkkkkKog...................',
      '..oKKKKKKKKo....................',
      '..oooooooooo....................',
      '......okko......................',
      '.oooookkooooooooooooooooooooooo.',
      '.oTTTTTTTTTTPPPPTTTTTTTTTTTTTTo.',
      '.oTTTTTTTTTTPPTaaaaaaaaaTTTTTto.',
      '.oTTTTTTTTTTTTTAAAAAAAAATTTTtto.',
      '.otttttttttttttttttttttttttttto.',
      '.oooooooooooooooooooooooooooooo.',
      '.oFFFFFFFFFFFFFFFFFFFFFFFFFFFFo.',
      '.oFFFFFFFFFFFFFFFFFFFFFFFFFFFfo.',
      '.ofFFFFFFFFFFFFFFFFFFFFFFFFFffo.',
      '.offffffffffffffffffffffffffffo.',
      '.oooooooooooooooooooooooooooooo.',
    ],
  ],
}


/**
 * A guest's table: a laptop open on it, lid towards the viewer, on the left where desks
 * keep their monitor, so the visitor's badge shows. Same size as a desk.
 */
export const GUEST_DESK: SpriteDef = {
  name: 'guest-desk',
  width: 32,
  height: 21,
  legend: {
    o: 'outline',
    j: 'metal.light',
    k: 'metal.mid',
    K: 'metal.dark',
    T: 'wood.light',
    t: 'wood.mid',
    F: 'wood.mid',
    f: 'wood.dark',
    a: 'key.light',
    A: 'key.dark',
    g: 'screen',
    G: 'glow',
    P: 'spill',
  },
  frames: [
    [
      '................................',
      '................................',
      '....oooooooooooo................',
      '....ojjjjjjjjjjogG..............',
      '....ojkkkkkkkkkogG..............',
      '....ojkkkkkkkkkogG..............',
      '....ojkkkaakkkkogG..............',
      '....ojkkkkkkkkkogG..............',
      '....okkkkkkkkkKog...............',
      '....oKKKKKKKKKKo................',
      '.oooooooooooooooooooooooooooooo.',
      '.oTTTTTTTTTTPPPPTTTTTTTTTTTTTTo.',
      '.oTTTTTTTTTTPPTTTTTTTTTTTTTTTto.',
      '.oTTTTTTTTTTTTTTTTTTTTTTTTTTtto.',
      '.otttttttttttttttttttttttttttto.',
      '.oooooooooooooooooooooooooooooo.',
      '.oFFFFFFFFFFFFFFFFFFFFFFFFFFFFo.',
      '.oFFFFFFFFFFFFFFFFFFFFFFFFFFFfo.',
      '.ofFFFFFFFFFFFFFFFFFFFFFFFFFffo.',
      '.offffffffffffffffffffffffffffo.',
      '.oooooooooooooooooooooooooooooo.',
    ],
  ],
}
