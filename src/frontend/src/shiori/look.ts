import type { Sky } from '../api/types'
import type { SkinId } from '../lab/presence/body/shapes'
import { HOURS, SKIES, type Hour, type WorldId } from '../lab/presence/world/types'
import { WORLD_ORDER } from '../lab/presence/world/worlds'

/** How Shiori and her world look for this viewer. */
export interface Look {
  skin: SkinId
  /** 0 smooth; otherwise screen pixels per art pixel. */
  pixel: number
  /** 'auto' follows the clock. */
  hour: Hour | 'auto'
  /** 'auto' follows the real weather of the person's town. */
  sky: Sky | 'auto'
  world: WorldId
  calm: boolean
}

export const SKINS: SkinId[] = ['swarm', 'medusa', 'portrait']
export const PIXELS = [0, 2, 3, 4]
export const LOOK_KEY = 'openspace.shiori.look'
export const DEFAULT_LOOK: Look = { skin: 'swarm', pixel: 3, hour: 'auto', sky: 'auto', world: 'city', calm: false }

/** Reads a stored look, keeping only values that still exist and falling back on the rest. */
export function parseLook(raw: string | null): Look {
  let v: Partial<Look> = {}
  try {
    const parsed: unknown = JSON.parse(raw ?? '{}')
    if (parsed && typeof parsed === 'object') v = parsed as Partial<Look>
  } catch {
    return DEFAULT_LOOK
  }
  return {
    skin: SKINS.includes(v.skin as SkinId) ? (v.skin as SkinId) : DEFAULT_LOOK.skin,
    pixel: PIXELS.includes(v.pixel as number) ? (v.pixel as number) : DEFAULT_LOOK.pixel,
    hour: v.hour === 'auto' || HOURS.includes(v.hour as Hour) ? (v.hour as Hour | 'auto') : DEFAULT_LOOK.hour,
    sky: v.sky === 'auto' || SKIES.includes(v.sky as Sky) ? (v.sky as Sky | 'auto') : DEFAULT_LOOK.sky,
    world: WORLD_ORDER.includes(v.world as WorldId) ? (v.world as WorldId) : DEFAULT_LOOK.world,
    calm: v.calm === true,
  }
}

export function loadLook(): Look {
  try {
    return parseLook(localStorage.getItem(LOOK_KEY))
  } catch {
    return DEFAULT_LOOK
  }
}

export function saveLook(look: Look) {
  try {
    localStorage.setItem(LOOK_KEY, JSON.stringify(look))
  } catch {
    // Private windows may refuse storage: the choice still holds for this visit.
  }
}
