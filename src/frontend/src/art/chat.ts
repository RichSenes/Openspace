import type { Walker } from '../activity/walks'
import type { SceneItem } from './scene'
import { pixelsOf, type SpriteDef } from './sprite'
import { walkerPixels, WALKER } from './walker'
import { characterColors, type Appearance } from './station'

/**
 * The made-up bubble of a chat between colleagues: round like a thought, with a tail of
 * dots, never a word inside; the real bubbles are boxes with text. Its doodles: dots, a
 * note, a heart, "!?" and a coffee.
 */
export const CHAT_BUBBLE: SpriteDef = {
  name: 'chat-bubble',
  width: 11,
  height: 10,
  legend: { o: 'outline', W: 'chat.paper' },
  frames: [
    [
      '..ooooooo..',
      '.oWWWWWWWo.',
      'oWWWWWWWWWo',
      'oWWWWWWWWWo',
      'oWWWWWWWWWo',
      'oWWWWWWWWWo',
      '.oWWWWWWWo.',
      '..ooooooo..',
      '..oo.......',
      'oo.........',
    ],
  ],
}

export const CHAT_GLYPHS: SpriteDef = {
  name: 'chat-glyphs',
  width: 7,
  height: 5,
  legend: { o: 'outline', b: 'chat.note', r: 'chat.heart', s: 'chat.steam', u: 'mug.light', U: 'mug.dark' },
  frames: [
    ['.......', '.......', '.o.o.o.', '.......', '.......'],
    ['....bb.', '....b.b', '....b..', '..bbb..', '..bb...'],
    ['.......', '.rr.rr.', '.rrrrr.', '..rrr..', '...r...'],
    ['.o..oo.', '.o...o.', '.o..o..', '.......', '.o..o..'],
    ['..s.s..', '.s.s...', '.uuuu..', '.uuuuU.', '.uuuu..'],
  ],
}

export const CHAT_COLORS: Record<string, string> = {
  'chat.paper': '#f4f1ea',
  'chat.note': '#4a6fb3',
  'chat.heart': '#d9574a',
  'chat.steam': '#b9b6c6',
}

/** An agent on its feet, at its place in a room whose corner is at (left, top), with its chat bubble on top of everything. */
export function walkerItems(look: Appearance, walker: Walker, left: number, top: number): SceneItem[] {
  const x = walker.x - left
  const y = walker.y - top
  const items: SceneItem[] = [
    { x, y, height: WALKER.height, pixels: walkerPixels(look, { facing: walker.facing, step: walker.step, sip: walker.sip, emotion: walker.chat ? 'happy' : undefined }) },
  ]
  if (walker.chat?.speaking) {
    const person = characterColors(look)
    const colors = (name: string) => CHAT_COLORS[name] ?? person(name)
    const bubble = [...pixelsOf(CHAT_BUBBLE, 0, colors), ...pixelsOf(CHAT_GLYPHS, walker.chat.glyph, colors, 2, 1)]
    // A huge height puts it after everything else in depth order, wherever it floats.
    items.push({ x: x + 14, y: y - 8, height: 10_000, pixels: bubble })
  }
  return items
}
