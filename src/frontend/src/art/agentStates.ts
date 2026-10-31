import { energyLevel } from './energy'
import type { Emotion } from './sheets/character'
import type { Signal } from './sheets/signals'
import { flashing, type Gesture, type ScreenState, type StationPose } from './station'

export type AgentState = 'idle' | 'working' | 'reading' | 'waiting' | 'queued' | 'done' | 'failed'

/** How each state of an agent looks at its desk. See docs/art-style.md. */
export const AGENT_LOOKS: Record<AgentState, { emotion: Emotion; gesture: Gesture; screen: ScreenState; signal?: Signal }> = {
  idle: { emotion: 'neutral', gesture: 'rest', screen: 'off' },
  working: { emotion: 'focused', gesture: 'typing', screen: 'on' },
  reading: { emotion: 'focused', gesture: 'think', screen: 'on' },
  waiting: { emotion: 'neutral', gesture: 'wave', screen: 'alert', signal: 'question' },
  queued: { emotion: 'asleep', gesture: 'rest', screen: 'off', signal: 'zz' },
  done: { emotion: 'happy', gesture: 'cheer', screen: 'on', signal: 'check' },
  failed: { emotion: 'worried', gesture: 'stuck', screen: 'off', signal: 'storm' },
}

/** Frames between two blinks: about four seconds at five frames per second. */
const BLINK_EVERY = 20

/** A tired agent yawns about every eight seconds, an exhausted one every five; a yawn lasts four frames. */
const YAWN_EVERY = { tired: 40, exhausted: 24 } as const
const YAWN_FRAMES = 4

/** A coffee break: three seconds sipping while the battery charges, then two seconds of a smile. */
export const COFFEE = { sip: 3, smile: 5 } as const

/** The energy of an agent: the room left in its conversation, and how long ago it got it back, in seconds. */
export interface Energy {
  left: number
  sinceRefresh?: number
}

/** Agents show tiredness and take their coffee while they work; other states keep their own look. */
const BUSY: ReadonlySet<AgentState> = new Set(['working', 'reading'])

/**
 * The pose of an agent in a state at a tick of the animation clock. Awake agents blink
 * now and then, alert screens flash. A momentary signal (an idea, a surprise) replaces
 * the state's own one while it lasts. With its energy known, a battery stands beside the
 * agent; a tired one has heavy eyes and yawns, an exhausted one also sinks into its chair
 * and types slower, and one that just got its energy back sips a coffee while it charges.
 */
export function poseOf(state: AgentState, tick: number, signal?: Signal, energy?: Energy): StationPose {
  const look = AGENT_LOOKS[state]
  const blinks = look.emotion !== 'asleep' && tick % BLINK_EVERY === BLINK_EVERY - 1
  const pose: StationPose = {
    emotion: blinks ? 'blink' : look.emotion,
    gesture: look.gesture,
    frame: tick,
    screen: flashing(look.screen, tick),
    signal: signal ?? look.signal,
  }
  if (!energy) return pose

  pose.energy = energy.left
  if (!BUSY.has(state)) return pose

  const since = energy.sinceRefresh
  if (since !== undefined && since >= 0 && since < COFFEE.smile) {
    if (since < COFFEE.sip) return { ...pose, gesture: 'sip', emotion: 'blink', energy: Math.min(energy.left, since / COFFEE.sip) }
    return { ...pose, emotion: blinks ? 'blink' : 'happy' }
  }

  const level = energyLevel(energy.left)
  if (level !== 'tired' && level !== 'exhausted') return pose
  const yawns = tick % YAWN_EVERY[level] < YAWN_FRAMES
  const tired = { ...pose, emotion: yawns ? 'yawn' : blinks ? 'blink' : 'tired' } as StationPose
  return level === 'exhausted' ? { ...tired, slump: 2, frame: Math.floor(tick / 2) } : tired
}
