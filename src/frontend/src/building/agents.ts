import type { DeskActivity, WorkTaskView } from '../api/types'
import type { AgentState } from '../art/agentStates'
import { CLOTH_COLORS, HAIR_COLORS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone } from '../art/palette'
import { HAIR_STYLES, type Accessory, type HairStyle } from '../art/sheets/character'
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
