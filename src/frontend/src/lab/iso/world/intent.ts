import type { CharacterKind, CharacterState, WorldView } from '../../../api/types'
import type { Bubble } from '../scene'
import type { Look, Pose } from '../scientist'

/**
 * What a real character does in the lab, from its state: at work it sits at its seat; waiting for
 * the person or failed, it queues at Shiori with an icon for what it brings; free, it lives its
 * social life; off (a routine switched off), it is not there.
 */
export type Intent = { where: 'seat'; pose: Pose } | { where: 'queue'; pose: Pose; bubble: Bubble } | { where: 'social' } | { where: 'away' }

/** The icon over someone queuing, for the kind of need they bring. */
export function bubbleFor(need: string | undefined, state: CharacterState): Bubble {
  if (state === 'Failed' || need === 'TaskFailed' || need === 'RoutineFailed') return 'failed'
  if (need === 'TaskToReview') return 'review'
  return 'question'
}

export function intentOf(state: CharacterState, need?: string): Intent {
  switch (state) {
    case 'Working':
      return { where: 'seat', pose: 'type' }
    case 'Waiting':
      return { where: 'queue', pose: 'notes', bubble: bubbleFor(need, state) }
    case 'Failed':
      // Downcast: standing still, the red mark over them.
      return { where: 'queue', pose: 'stand', bubble: 'failed' }
    case 'Off':
      return { where: 'away' }
    default:
      return { where: 'social' }
  }
}

/**
 * Who queues where at Shiori: the oldest need first, closest to her front; each character
 * once, with the kind of what it brings.
 */
export function queueOf(view: WorldView, present: Set<string>): { id: string; need: string }[] {
  const seen = new Set<string>()
  const out: { id: string; need: string }[] = []
  for (const w of [...view.waiting].sort((x, y) => x.since.localeCompare(y.since))) {
    if (!w.characterId || !present.has(w.characterId) || seen.has(w.characterId)) continue
    seen.add(w.characterId)
    out.push({ id: w.characterId, need: w.kind })
  }
  return out
}

const COATS: Record<CharacterKind, string | undefined> = {
  // Desk agents: the lab's white coat, over their office's colour.
  Desk: undefined,
  // Interns: a light blue coat.
  Intern: '#a9d4f2',
  // Routines: a grey technician's overall.
  Routine: '#8d959e',
}

const SKINS = ['#f1c9a5', '#e8b48f', '#8d5a3b', '#f6d7c3', '#c98e66', '#5e3b28', '#f3d1b5']
const HAIRS = ['#1d1a22', '#5a3424', '#141018', '#c9a06a', '#2a2a33', '#8a4a2a', '#d8d0c0']
const STYLES: Look['style'][] = ['short', 'long', 'curly', 'bun', 'pony']

/** A character's looks, always the same for the same id; the coat tells desk, intern and routine apart. */
export function lookOf(id: string, kind: CharacterKind, accent: string): Look {
  let h = 2166136261
  for (let i = 0; i < id.length; i++) h = Math.imul(h ^ id.charCodeAt(i), 16777619)
  const pick = <T>(list: T[], salt: number) => list[(((h >>> 0) ^ Math.imul(salt, 2654435761)) >>> 0) % list.length]
  return {
    skin: pick(SKINS, 1),
    hair: pick(HAIRS, 2),
    style: pick(STYLES, 3),
    shirt: accent,
    glasses: pick([true, false, false], 4),
    coat: COATS[kind],
    trousers: kind === 'Routine' ? '#5d656e' : undefined,
  }
}
