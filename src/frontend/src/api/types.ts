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
  repositoryPath: string | null
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
  branch: string | null
  notes: TaskNoteView[]
}

/** What a desk is doing right now: the data behind a speech bubble. */
export interface DeskActivity {
  officeId: string
  deskId: string
  taskId: string
  kind: 'Working' | 'Waiting' | 'Stopped'
  step: string | null
  doing: string | null
  permissionId: string | null
  at: string
  headline: string
}
