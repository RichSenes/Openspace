import type { DeskActivity, SoundRule, SoundsView } from '../api/types'

export type SoundEvent =
  | 'needs-you'
  | 'question'
  | 'review'
  | 'failed'
  | 'verdict'
  | 'plan-ready'
  | 'checks-failed'
  | 'paused'
  | 'all-done'
  | 'offline'

export const SOUND_EVENTS: { id: SoundEvent; label: string }[] = [
  { id: 'needs-you', label: 'An agent asks for a permission' },
  { id: 'question', label: 'An agent asks you to do something' },
  { id: 'review', label: 'Work is ready for review' },
  { id: 'verdict', label: "Claude's review of the work is in" },
  { id: 'failed', label: 'A task failed' },
  { id: 'plan-ready', label: "The supervisor's plan is ready" },
  { id: 'checks-failed', label: 'Checks after a merge failed' },
  { id: 'paused', label: 'An account is paused for its limits' },
  { id: 'all-done', label: 'An office has nothing left to do' },
  { id: 'offline', label: 'The connection to Openspace was lost' },
]

/** Sounds drawn in code, like the pixel art: no files, no licences. */
export const BUILTINS = {
  chime: 'Chime',
  knock: 'Knock',
  levelUp: 'Level up',
  fail: 'Falling tones',
  coin: 'Coin',
  blip: 'Blip',
  powerDown: 'Power down',
  fanfare: 'Fanfare',
  alarm: 'Alarm',
} as const

export type BuiltinSound = keyof typeof BUILTINS

export const DEFAULT_SOUND: Record<SoundEvent, string> = {
  'needs-you': 'builtin:chime',
  question: 'builtin:knock',
  review: 'builtin:levelUp',
  verdict: 'builtin:blip',
  failed: 'builtin:fail',
  'plan-ready': 'builtin:coin',
  'checks-failed': 'builtin:alarm',
  paused: 'builtin:powerDown',
  'all-done': 'builtin:fanfare',
  // Openspace is restarted often while it is built: quiet unless chosen.
  offline: 'silent',
}

/** A moment worth a sound, from the live updates. */
export interface Cue {
  event: SoundEvent
  officeId?: string | null
  deskId?: string | null
  /** The same key plays once: a request shown again after a reload stays quiet. */
  key: string
}

/** The sound a moment plays at a desk: the desk's rule, else its room's, else the general one, else the default. */
export function resolveSound(config: Pick<SoundsView, 'rules'> | undefined, event: SoundEvent, deskId?: string | null, roomId?: string | null): string {
  const rules: SoundRule[] = (config?.rules ?? []).filter((r) => r.event === event)
  const desk = deskId ? rules.find((r) => r.deskId === deskId) : undefined
  const room = roomId ? rules.find((r) => !r.deskId && r.roomId === roomId) : undefined
  const general = rules.find((r) => !r.deskId && !r.roomId)
  return (desk ?? room ?? general)?.sound ?? DEFAULT_SOUND[event]
}

/** The words for a sound, as menus show it. */
export function soundLabel(sound: string): string {
  if (sound === 'silent') return 'Silent'
  if (sound.startsWith('builtin:')) return BUILTINS[sound.slice(8) as BuiltinSound] ?? sound
  return sound.startsWith('file:') ? sound.slice(5) : sound
}

/** A desk starting to wait for you is a moment: a permission or a question. */
export function cueOfActivity(activity: DeskActivity): Cue | null {
  if (activity.kind !== 'Waiting' || !activity.permissionId) return null
  return { event: activity.question ? 'question' : 'needs-you', officeId: activity.officeId, deskId: activity.deskId, key: activity.permissionId }
}

const CUE = 'openspace-cue'

/** Announces a moment to whoever plays sounds in this page. */
export function emitCue(cue: Cue) {
  window.dispatchEvent(new CustomEvent<Cue>(CUE, { detail: cue }))
}

export function onCue(listener: (cue: Cue) => void): () => void {
  const handler = (e: Event) => listener((e as CustomEvent<Cue>).detail)
  window.addEventListener(CUE, handler)
  return () => window.removeEventListener(CUE, handler)
}
