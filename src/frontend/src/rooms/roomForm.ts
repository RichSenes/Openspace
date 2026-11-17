import type { RoomView } from '../api/types'
import { formatSteps, parseSteps } from './procedure'

/** A room's settings as its form edits them before one save. */
export interface RoomForm {
  context: string
  /** One step per line, as typed. */
  procedure: string
  check: string
}

export type RoomPart = keyof RoomForm

export function roomFormOf(room: Pick<RoomView, 'context' | 'procedure' | 'check'>): RoomForm {
  return { context: room.context, procedure: formatSteps(room.procedure), check: room.check ?? '' }
}

/** What the form changes: steps compared as saved (markers and blanks dropped), the check trimmed. */
export function roomChanges(room: Pick<RoomView, 'context' | 'procedure' | 'check'>, form: RoomForm): RoomPart[] {
  const parts: RoomPart[] = []
  if (form.context !== room.context) parts.push('context')
  if (formatSteps(parseSteps(form.procedure)) !== formatSteps(room.procedure)) parts.push('procedure')
  if (form.check.trim() !== (room.check ?? '')) parts.push('check')
  return parts
}
