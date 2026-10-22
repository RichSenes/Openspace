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

/** The desk with its monitor; 'g' is the screen, coloured by the desk's state. */
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
  },
  frames: [
    [
      '..oooooooooo....................',
      '..ojjjjjjjkog...................',
      '..ojkkkkkkkog...................',
      '..ojkkkkkkkog...................',
      '..ojkkkkkkkog...................',
      '..ojkkkkkkkog...................',
      '..okkkkkkkKog...................',
      '..oKKKKKKKKo....................',
      '..oooooooooo....................',
      '......okko......................',
      '.oooookkooooooooooooooooooooooo.',
      '.oTTTTTTTTTTTTTTTTTTTTTTTTTTTTo.',
      '.oTTTTTTTTTTTTTaaaaaaaaaTTTTTto.',
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
