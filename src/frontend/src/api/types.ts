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
  roleId: string | null
}

export interface RoomView {
  id: string
  name: string
  context: string
  procedure: string[]
  desks: DeskView[]
}

export interface ProcedureStepView {
  number: number
  text: string
  evidence: string | null
  completedAt: string | null
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
  mergedInto: string | null
  epicId: string | null
  procedure: ProcedureStepView[]
  notes: TaskNoteView[]
}

export type AuditKind = 'Lifecycle' | 'Step' | 'Read' | 'Change' | 'Command' | 'Search' | 'Permission' | 'Note' | 'Procedure' | 'Other'

export interface AuditEntryView {
  id: number
  at: string
  kind: AuditKind
  summary: string
  detail: string | null
  succeeded: boolean | null
}

export interface BranchList {
  current: string | null
  branches: string[]
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

export type PermissionLevel = 'Allow' | 'Ask' | 'Deny'

export interface RoleView {
  id: string
  name: string
  description: string
  instructions: string
  model: string | null
  edit: PermissionLevel
  bash: PermissionLevel
  web: PermissionLevel
}

export type RoleInput = Omit<RoleView, 'id'>

export interface EpicView {
  id: string
  officeId: string
  title: string
  request: string
  requestedBy: string
  createdAt: string
}

export interface PlanView {
  epic: EpicView
  tasks: WorkTaskView[]
}

export interface ReportView {
  id: string
  officeId: string
  kind: 'Period' | 'Epic'
  title: string
  body: string
  requestedBy: string
  createdAt: string
  periodStart: string | null
  periodEnd: string | null
  epicId: string | null
  writtenByAi: boolean
}
