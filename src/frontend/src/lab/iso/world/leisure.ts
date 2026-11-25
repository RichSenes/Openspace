import type { Pose } from '../scientist'
import { hashOf, type Flavor } from './layout'

/**
 * Free time away from the desk. Each office has its own comfort corner (two of the kinds below,
 * picked from its name and its kind of work, so no two offices look alike), and whoever is free
 * chooses how to spend the time by character: a game in the break room, a look round the other
 * offices, a chat with a colleague at work, the comfort corner, or something alone (the phone,
 * a book, a coffee, a stretch).
 */

export type Comfort = 'reading' | 'sofa' | 'beanbags' | 'cooler' | 'kitchen' | 'aquarium'

export const COMFORTS: Comfort[] = ['reading', 'sofa', 'beanbags', 'cooler', 'kitchen', 'aquarium']

/** What the kind of work leans to: researchers read, creatives sprawl, coders get coffee. */
const LEANS: Record<Flavor, Comfort[]> = {
  code: ['kitchen', 'beanbags', 'cooler'],
  research: ['reading', 'aquarium', 'cooler'],
  creative: ['beanbags', 'sofa', 'aquarium'],
  data: ['sofa', 'reading', 'kitchen'],
}

/** An office's comfort corner: two different kinds, the same every time for the same office. */
export function comfortsFor(name: string, flavor: Flavor): Comfort[] {
  const first = LEANS[flavor][Math.floor(hashOf(name, 41) * LEANS[flavor].length)]
  const rest = COMFORTS.filter((c) => c !== first)
  const second = rest[Math.floor(hashOf(name, 43) * rest.length)]
  return [first, second]
}

export type Pastime = 'games' | 'tour' | 'visit' | 'nook' | 'alone'

const BASE: Record<Pastime, number> = { games: 0.26, tour: 0.18, visit: 0.14, nook: 0.22, alone: 0.2 }

/** How much each trait pulls towards each pastime. */
const PULL: Record<string, Partial<Record<Pastime, number>>> = {
  Chatty: { visit: 2, games: 1.3, alone: 0.6 },
  Shy: { visit: 0.3, games: 0.6, nook: 1.6, alone: 1.5 },
  Curious: { tour: 2.2 },
  Calm: { nook: 1.5, games: 0.7 },
  Bold: { games: 1.6 },
  Sunny: { visit: 1.4, games: 1.2 },
  Meticulous: { tour: 1.3, alone: 1.2 },
  DryHumoured: { alone: 1.3, visit: 0.8 },
}

export function pastimeWeights(traits: string[]): Record<Pastime, number> {
  const w = { ...BASE }
  for (const t of traits) for (const [k, f] of Object.entries(PULL[t] ?? {})) w[k as Pastime] *= f
  const sum = Object.values(w).reduce((s, x) => s + x, 0)
  for (const k of Object.keys(w) as Pastime[]) w[k] /= sum
  return w
}

/** One pastime, from a roll in [0, 1). */
export function choosePastime(traits: string[], roll: number): Pastime {
  const w = pastimeWeights(traits)
  let acc = 0
  for (const k of Object.keys(w) as Pastime[]) {
    acc += w[k]
    if (roll < acc) return k
  }
  return 'alone'
}

/** Something to do alone, wherever they are, from a roll in [0, 1). */
export function aloneAct(roll: number): { pose: Pose; secs: [number, number] } {
  if (roll < 0.38) return { pose: 'phone', secs: [15, 35] }
  if (roll < 0.66) return { pose: 'book', secs: [25, 45] }
  if (roll < 0.86) return { pose: 'coffee', secs: [12, 25] }
  return { pose: 'stretch', secs: [5, 9] }
}
