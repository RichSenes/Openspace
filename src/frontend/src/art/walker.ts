import { FAN_COSTUMES } from './fanPack'
import { ACCESSORIES, BODY, COSTUMES, FACES, GESTURES, HAIR_STYLES, HEAD, OUTFITS, type Emotion, type HairStyle } from './sheets/character'
import { LEGS } from './sheets/standing'
import { pixelsOf, type Pixel, type SpriteDef } from './sprite'
import { characterColors, type Appearance } from './station'

/** A standing agent is the 24 by 32 frame of the seated one, legs and all. */
export const WALKER = { width: 24, height: 32 } as const

/** Facing the room, or showing its back: walking away, or busy at a shelf or a board. */
export type Facing = 'front' | 'back'

export interface WalkerPose {
  facing: Facing
  /** Frame of the legs: 0 and 2 stand, 1 and 3 step. */
  step: number
  emotion?: Emotion | 'blink'
  /** Holding a cup to the mouth, on a break. */
  sip?: boolean
}

/** Rows of the head above the neck: from the back they are all hair. */
const SKULL_ROWS = 15

/**
 * The back of a head with a hairstyle: head and hair as one silhouette, filled with hair
 * from edge to edge and shaded on the right, the neck still skin below it. Made from the
 * front drawings, so every hairstyle gets its back for free.
 */
function backHead(style: HairStyle): SpriteDef {
  const hair = HAIR_STYLES[style].frames[0]
  const rows = HEAD.frames[0].map((row, y) => [...row].map((c, x) => (hair[y][x] !== '.' ? hair[y][x] : c)))
  const topmost = new Set<number>()
  for (let y = 0; y < SKULL_ROWS; y++) {
    const row = rows[y]
    const first = row.findIndex((c) => c !== '.')
    if (first < 0) continue
    const last = row.length - 1 - [...row].reverse().findIndex((c) => c !== '.')
    for (let x = first + 1; x < last; x++) {
      // The first pixel of each column from the top keeps its outline.
      if (!topmost.has(x)) {
        topmost.add(x)
        if (row[x] === 'o') continue
      }
      row[x] = x === last - 1 ? 'h' : 'H'
    }
    topmost.add(first).add(last)
  }
  return { ...HEAD, name: `back-${style}`, frames: [rows.map((r) => r.join(''))] }
}

/** The back of a shirt or an outfit: the collar's skin, zips and strings become plain cloth. */
function backBody(body: SpriteDef): SpriteDef {
  const plain = (row: string) => {
    const counts = new Map<string, number>()
    for (const c of row) if (c !== '.' && c !== 'o') counts.set(c, (counts.get(c) ?? 0) + 1)
    return [...counts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? 'o'
  }
  return { ...body, name: `back-${body.name}`, frames: body.frames.map((f) => f.map((row) => row.replace(/[zZw]/g, plain(row)))) }
}

const backs = new Map<string, SpriteDef>()
const memo = (key: string, make: () => SpriteDef) => {
  let sprite = backs.get(key)
  if (!sprite) backs.set(key, (sprite = make()))
  return sprite
}

/**
 * An agent on its feet, in its own 24 by 32 pixels: legs, then body, head, face, hair and
 * what it wears, as at its desk. From the back the face and glasses are hidden. Costumes
 * have no back: they always face the room.
 */
export function walkerPixels(look: Appearance, pose: WalkerPose): Pixel[] {
  const person = characterColors(look)
  const legs = pixelsOf(LEGS, pose.step % LEGS.frames.length, person)
  const costume = look.costume && (FAN_COSTUMES?.[look.costume] ?? COSTUMES[look.costume])
  if (costume) return [...legs, ...pixelsOf(costume.body, 0, person), ...pixelsOf(costume.open, 0, person)]
  const body = look.outfit ? OUTFITS[look.outfit] : BODY
  const accessory = look.accessory && ACCESSORIES[look.accessory]
  const layers: (SpriteDef | undefined)[] =
    pose.facing === 'back'
      ? [memo(`body-${body.name}`, () => backBody(body)), memo(`head-${look.hairStyle}`, () => backHead(look.hairStyle)), look.accessory === 'glasses' ? undefined : accessory]
      : [body, HEAD, FACES[pose.emotion ?? 'neutral'], HAIR_STYLES[look.hairStyle], accessory, pose.sip ? GESTURES.sip : undefined]
  return [...legs, ...layers.flatMap((sprite) => (sprite ? pixelsOf(sprite, 0, person) : []))]
}
