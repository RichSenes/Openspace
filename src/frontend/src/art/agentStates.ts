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

/**
 * The pose of an agent in a state at a tick of the animation clock. Awake agents blink
 * now and then, alert screens flash. A momentary signal (an idea, a surprise) replaces
 * the state's own one while it lasts.
 */
export function poseOf(state: AgentState, tick: number, signal?: Signal): StationPose {
  const look = AGENT_LOOKS[state]
  const blinks = look.emotion !== 'asleep' && tick % BLINK_EVERY === BLINK_EVERY - 1
  return {
    emotion: blinks ? 'blink' : look.emotion,
    gesture: look.gesture,
    frame: tick,
    screen: flashing(look.screen, tick),
    signal: signal ?? look.signal,
  }
}
