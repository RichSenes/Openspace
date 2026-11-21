/**
 * The one state machine behind Shiori's face, whatever face draws it. The session sends
 * events; the face only reads the state and the look that goes with it.
 */
export type FaceState = 'idle' | 'listening' | 'thinking' | 'speaking' | 'pleased' | 'unsure'

export type Mood = 'pleased' | 'unsure' | 'neutral'

export type FaceEvent =
  | { type: 'hear' } // the person is typing or talking
  | { type: 'send' } // the person sent a message: Shiori reads and thinks
  | { type: 'speak' } // the answer starts streaming
  | { type: 'finish'; mood: Mood } // the answer ended, with how Shiori feels about it
  | { type: 'settle' } // a feeling has been shown long enough
  | { type: 'reset' }

export const FACE_STATES: FaceState[] = ['idle', 'listening', 'thinking', 'speaking', 'pleased', 'unsure']

export function faceReducer(state: FaceState, event: FaceEvent): FaceState {
  switch (event.type) {
    case 'reset':
      return 'idle'
    case 'hear':
      // Typing never interrupts an answer being spoken.
      return state === 'speaking' || state === 'thinking' ? state : 'listening'
    case 'send':
      return 'thinking'
    case 'speak':
      return state === 'thinking' || state === 'listening' || state === 'idle' ? 'speaking' : state
    case 'finish':
      if (state !== 'speaking') return state
      return event.mood === 'neutral' ? 'idle' : event.mood
    case 'settle':
      return state === 'pleased' || state === 'unsure' ? 'idle' : state
  }
}

/** How each face should look in a state: numbers every face maps onto its own drawing. */
export interface FaceLook {
  label: string
  /** What the state means, in a few words. */
  hint: string
  /** 0..1, how bright and big. */
  energy: number
  /** Multiplier of the face's own motion. */
  speed: number
  /** Main colour of the state. */
  color: string
  /** -1..1, where the gaze goes horizontally (1 = toward the chat). */
  gazeX: number
  /** -1..1, where the gaze goes vertically (-1 = up). */
  gazeY: number
}

export const FACE_LOOKS: Record<FaceState, FaceLook> = {
  idle: { label: 'Resting', hint: 'Waiting for you', energy: 0.35, speed: 0.5, color: '#82a8f5', gazeX: 0.2, gazeY: 0 },
  listening: { label: 'Listening', hint: 'Following what you type', energy: 0.7, speed: 0.8, color: '#7fd4e8', gazeX: 1, gazeY: 0.15 },
  thinking: { label: 'Thinking', hint: 'Reading the notes it needs', energy: 0.6, speed: 1.8, color: '#c3a6f7', gazeX: -0.5, gazeY: -0.8 },
  speaking: { label: 'Answering', hint: 'Answering, with its sources', energy: 0.85, speed: 1.2, color: '#9cc0ff', gazeX: 0.8, gazeY: 0 },
  pleased: { label: 'Pleased', hint: 'Happy with where this landed', energy: 0.9, speed: 0.9, color: '#f2b84b', gazeX: 0.6, gazeY: -0.1 },
  unsure: { label: 'Not sure', hint: 'Has a doubt worth raising', energy: 0.45, speed: 0.6, color: '#f7a072', gazeX: -0.7, gazeY: 0.3 },
}

/** Eases one look into another so a face never jumps between states. */
export function blendLook(from: FaceLook, to: FaceLook, k: number): FaceLook {
  const t = Math.min(1, Math.max(0, k))
  const mix = (a: number, b: number) => a + (b - a) * t
  return {
    label: to.label,
    hint: to.hint,
    energy: mix(from.energy, to.energy),
    speed: mix(from.speed, to.speed),
    color: mixHex(from.color, to.color, t),
    gazeX: mix(from.gazeX, to.gazeX),
    gazeY: mix(from.gazeY, to.gazeY),
  }
}

export function hexToRgb(hex: string): [number, number, number] {
  const n = parseInt(hex.slice(1), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

export function mixHex(a: string, b: string, t: number): string {
  const [ar, ag, ab] = hexToRgb(a)
  const [br, bg, bb] = hexToRgb(b)
  const c = (x: number, y: number) => Math.round(x + (y - x) * t).toString(16).padStart(2, '0')
  return `#${c(ar, br)}${c(ag, bg)}${c(ab, bb)}`
}
