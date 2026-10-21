export const TASK_STATUSES = [
  'Proposed',
  'Queued',
  'Running',
  'Review',
  'Failed',
  'Done',
  'Discarded',
] as const

export type TaskStatus = (typeof TASK_STATUSES)[number]

export interface DeskView {
  id: string
  name: string
  agentName: string
  model: string | null
}

export interface RoomView {
  id: string
  name: string
  context: string
  desks: DeskView[]
}

export interface OfficeView {
  id: string
  name: string
  rooms: RoomView[]
}

export interface TaskNoteView {
  authorKind: 'Human' | 'Desk' | 'System'
  authorName: string | null
  deskId: string | null
  text: string
  writtenAt: string
}

export interface WorkTaskView {
  id: string
  officeId: string
  deskId: string | null
  title: string
  description: string
  requestedBy: string
  status: TaskStatus
  createdAt: string
  notes: TaskNoteView[]
}
