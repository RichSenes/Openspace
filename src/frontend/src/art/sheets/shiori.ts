// Shiori on her feet, in the 24 by 32 frame of every standing agent, over the same legs.
// Every string is one row of pixels; '.' is transparent. See docs/art-style.md.
import type { SpriteDef } from '../sprite'
import { CHARACTER_LEGEND } from './character'

/** The agents' letters, plus the sheen of her hair, her sash, the gold of her pins, jade and plum blossom. */
export const SHIORI_LEGEND = {
  ...CHARACTER_LEGEND,
  l: 'hair.sheen',
  R: 'sash.light',
  r: 'sash.dark',
  Y: 'pin.gold',
  G: 'jade',
  P: 'blossom',
  X: 'blossom.heart',
} as const

const shiori = (name: string, rows: string[], more: string[][] = []): SpriteDef => ({
  name: `shiori-${name}`,
  width: 24,
  height: 32,
  legend: SHIORI_LEGEND,
  frames: [pad(rows), ...more.map(pad)],
})

/** Rows not written are empty, so each drawing lists only the rows it uses. */
function pad(rows: string[]): string[] {
  return [...rows, ...Array<string>(32 - rows.length).fill('........................')]
}

const EMPTY = '........................'
const blank = (n: number) => Array<string>(n).fill(EMPTY)

/**
 * Her hair from the front, over the agents' head: drawn up into a bun at the centre with
 * two gold pins crossed through it, the forehead clear, two locks falling in front of the
 * ears down to the jaw, a jade drop at each ear.
 */
export const SHIORI_HAIR = shiori('hair', [
  '.......Y..oooo..Y.......',
  '........YoHllHoY........',
  '.........oHHhho.........',
  '........ooHhhHoo........',
  '.......oHHHlHHHHo.......',
  '......oHHHlHHHHHho......',
  '......oHHlHHHHHHho......',
  '......oHHh....hHHo......',
  '......oHh......hHo......',
  '.....oHH........HHo.....',
  '.....oHh........hHo.....',
  '.....oHo........oHo.....',
  '....GoHo........oHoG....',
  '.....oHo........oHo.....',
  '.....oHo........oHo.....',
  '......o..........o......',
])

/**
 * Her dress from the front: a dark wrap dress, its V neckline crossing to one side, a
 * sprig of plum blossom on the shoulder, the red sash tied at the side with its ends
 * hanging, the skirt to below the knee over dark tights.
 */
export const SHIORI_DRESS = shiori('dress', [
  ...blank(17),
  '....oooooqSzzSqooooo....',
  '...oCCCCCCqSSqCCCPCCo...',
  '...oCCCCCCCqqCCPXPCco...',
  '..oCCCCCCCCCqCCCPCCcco..',
  '..oCcCCCCCCCCqCCCCCcco..',
  '..oCcRRRRRRRRRRRRRRrco..',
  '..oCcrrrrrrrrrrrrRrrco..',
  '..ocqcccccccccccRrcqqo..',
  '.....occccccccccRro.....',
  '.....occcccccccccqro....',
  '....oqcccccccccccqqo....',
  '....oooooooooooooooo....',
])

/** From behind: the hair drawn up from the nape, the bun and its pins, the plain back of the dress and its sash. */
export const SHIORI_BACK = shiori('back', [
  '.......Y..oooo..Y.......',
  '........YoHllHoY........',
  '.........oHHhho.........',
  '........ooHhhHoo........',
  '.......oHHHlHHHHo.......',
  '......oHHlHHHHHHho......',
  '......oHlHHHHHHHho......',
  '......oHHHHHHHHHho......',
  '......oHHHHHHHHHho......',
  '.....oHHHHHHHHHHhho.....',
  '.....oHHHHHHHHHHhho.....',
  '......oHHHHHHHHhho......',
  '....GoHhHHHHHHhhhoG.....',
  '.......oohhhhhhoo.......',
  '........osSSSSso........',
  '..........ozzo..........',
  '..........ozzo..........',
  '....ooooooqqqqoooooo....',
  '...oCCCCCCCCCCCCCCCCo...',
  '...oCCCCCCCCCCCCCCCco...',
  '..oCCCCCCCCCCCCCCCCcco..',
  '..oCcCCCCCCCCCCCCCCcco..',
  '..oCcRRRRRRRRRRRRRRrco..',
  '..oCcrrrrrrrrrrrrrrrco..',
  '..ocqcccccccccccccqqqo..',
  '.....occcccccccccccco...',
  '.....occcccccccccccqo...',
  '....oqcccccccccccccqo...',
  '....oooooooooooooooo....',
])

/** Her hair walking across, facing right: the bun and pins on top, the hair drawn up behind the ear, one lock in front of it. */
export const SHIORI_SIDE_HAIR = shiori('side-hair', [
  '.......Y.oooo.Y.........',
  '........YoHlHoY.........',
  '.........oHhho..........',
  '........oohhhoo.........',
  '.......oHHHHHHHoo.......',
  '......oHHHHlHHHHHo......',
  '.....oHHHHHHlHHHHo......',
  '.....oHHHHHHHhh.........',
  '.....oHHHHHHh...........',
  '.....oHHh..H............',
  '.....oHHh..H............',
  '......oHh..H............',
  '......ohh.GH............',
  '.......oh..H............',
  '...........H............',
  '...........o............',
])

/** Her dress walking across, facing right: the wrap's edge, the blossom, the sash, the skirt over the legs. */
export const SHIORI_SIDE_DRESS = shiori('side-dress', [
  ...blank(16),
  '........ooozzoooo.......',
  '.......oCCCqSCCCCCo.....',
  '.......oCCCCqCPCCCo.....',
  '.......oCCCCCqCCCco.....',
  '.......oCCCCCCqCCco.....',
  '.......oRRRRRRRRRRo.....',
  '.......orrrrrrrrrro.....',
  '.......oCCCCCCCCcco.....',
  '.......occcccccccqo.....',
  '.......occcccccccqo.....',
  '......occcccccccccqo....',
  '......occcccccccccqo....',
  '......oooooooooooooo....',
])

/** Talking at a desk, facing right: the near forearm forward as she explains, the hand turning up, then flat. */
export const SHIORI_EXPLAIN = shiori(
  'explain',
  [
    ...blank(17),
    '..........occo..........',
    '..........oCco....ooo...',
    '..........oCcoooooSSo...',
    '..........oCCCCCCcSSo...',
    '..........ooooooooooo...',
  ],
  [
    [
      ...blank(17),
      '..........occo..........',
      '..........oCco..........',
      '..........oCcoooooooo...',
      '..........oCCCCCCcSSo...',
      '..........ooooooooooo...',
    ],
  ],
)

/** The made-up bubble of her talk: one icon for the news she brings, never a word. */
export const SHIORI_GLYPHS: SpriteDef = {
  name: 'shiori-glyphs',
  width: 7,
  height: 5,
  legend: { o: 'outline', g: 'glyph.go', r: 'glyph.fail', a: 'glyph.ask', k: 'glyph.done' },
  frames: [
    // started: a play mark
    ['..g....', '..gg...', '..ggg..', '..gg...', '..g....'],
    // failed: a cross
    ['.r...r.', '..r.r..', '...r...', '..r.r..', '.r...r.'],
    // waiting for the person: a question mark
    ['..aaa..', '.a...a.', '...aa..', '.......', '...a...'],
    // to review: a tick
    ['.......', '......k', '.....k.', 'k..kk..', '.kk....'],
  ],
}
