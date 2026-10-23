import type { DeskActivity, DeskLook, WorkTaskView } from '../api/types'
import type { AgentState } from '../art/agentStates'
import { CLOTH_COLORS, HAIR_COLORS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone } from '../art/palette'
import { ACCESSORIES, COSTUMES, HAIR_STYLES, OUTFITS, type Accessory, type Costume, type HairStyle, type Outfit } from '../art/sheets/character'
import type { Appearance } from '../art/station'

/** A small, stable hash of a string: the same desk always gets the same number. */
function hash(text: string): number {
  let h = 2166136261
  for (let i = 0; i < text.length; i++) {
    h ^= text.charCodeAt(i)
    h = Math.imul(h, 16777619)
  }
  return h >>> 0
}

const pick = <T,>(list: readonly T[], seed: number) => list[seed % list.length]

/**
 * How the agent at a desk looks, until it can be chosen: picked from the desk's id, so
 * every desk keeps its person across reloads and machines.
 */
export function appearanceOf(deskId: string): Appearance {
  const h = hash(deskId)
  // Homages (the pointy hat) are for the agents someone picks them for, not left to chance.
  const accessories: (Accessory | undefined)[] = [undefined, undefined, 'glasses', 'headset']
  return {
    skin: pick(Object.keys(SKIN_TONES) as SkinTone[], h),
    hair: pick(Object.keys(HAIR_COLORS) as HairColor[], h >>> 3),
    hairStyle: pick(Object.keys(HAIR_STYLES) as HairStyle[], h >>> 7),
    cloth: pick((Object.keys(CLOTH_COLORS) as ClothColor[]).filter((c) => c !== 'black'), h >>> 11),
    accessory: pick(accessories, h >>> 15),
  }
}

/** Everything a look can be made of, in the order the editor offers it. */
export const LOOK_PARTS = {
  skin: Object.keys(SKIN_TONES) as SkinTone[],
  hair: Object.keys(HAIR_COLORS) as HairColor[],
  hairStyle: Object.keys(HAIR_STYLES) as HairStyle[],
  cloth: Object.keys(CLOTH_COLORS) as ClothColor[],
  accessory: Object.keys(ACCESSORIES) as Accessory[],
  outfit: Object.keys(OUTFITS) as Outfit[],
  costume: Object.keys(COSTUMES) as Costume[],
}

const known = <T extends string>(list: readonly T[], value: string | null | undefined): T | undefined =>
  list.find((item) => item === value)

/**
 * How the agent at a desk looks: as chosen by hand, part by part, and as picked from the
 * desk's id for whatever was not chosen or is not known to this version.
 */
export function lookOf(desk: { id: string; look?: DeskLook | null }): Appearance {
  const picked = appearanceOf(desk.id)
  const look = desk.look
  if (!look) return picked
  return {
    skin: known(LOOK_PARTS.skin, look.skin) ?? picked.skin,
    hair: known(LOOK_PARTS.hair, look.hair) ?? picked.hair,
    hairStyle: known(LOOK_PARTS.hairStyle, look.hairStyle) ?? picked.hairStyle,
    cloth: known(LOOK_PARTS.cloth, look.cloth) ?? picked.cloth,
    accessory: known(LOOK_PARTS.accessory, look.accessory),
    outfit: known(LOOK_PARTS.outfit, look.outfit),
    costume: known(LOOK_PARTS.costume, look.costume),
  }
}

/** An appearance as the API stores it. */
export const deskLookOf = (look: Appearance): DeskLook => ({
  skin: look.skin,
  hair: look.hair,
  hairStyle: look.hairStyle,
  cloth: look.cloth,
  accessory: look.accessory ?? null,
  outfit: look.outfit ?? null,
  costume: look.costume ?? null,
})

/** A look at random, for the shuffle button: no costume, which is chosen on purpose. */
export function shuffledLook(random: () => number = Math.random): Appearance {
  const any = <T,>(list: readonly T[]) => list[Math.floor(random() * list.length)]
  return {
    skin: any(LOOK_PARTS.skin),
    hair: any(LOOK_PARTS.hair),
    hairStyle: any(LOOK_PARTS.hairStyle),
    cloth: any(LOOK_PARTS.cloth),
    accessory: any([undefined, undefined, ...LOOK_PARTS.accessory.filter((a) => a !== 'pointyHat')]),
    outfit: any([undefined, ...LOOK_PARTS.outfit]),
  }
}

/** Steps that only look at things rather than change them. */
const LOOKING = /^(Reading|Searching|Looking|Fetching|Listing)\b/

/**
 * What the agent at a desk is up to: waiting for you comes first, then working, then
 * the outcome of its latest task that still needs a person (failed, to review), then
 * work queued for a seat.
 */
export function agentStateOf(deskId: string, tasks: readonly WorkTaskView[], activity: DeskActivity | undefined): AgentState {
  if (activity?.kind === 'Waiting') return 'waiting'
  const own = tasks.filter((t) => t.deskId === deskId)
  if (activity?.kind === 'Working' || own.some((t) => t.status === 'Running')) {
    return LOOKING.test(activity?.doing ?? '') ? 'reading' : 'working'
  }
  const latest = [...own].sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0]
  if (latest?.status === 'Failed') return 'failed'
  if (own.some((t) => t.status === 'Review')) return 'done'
  if (own.some((t) => t.status === 'Queued')) return 'queued'
  return 'idle'
}
