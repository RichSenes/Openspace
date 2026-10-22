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
  /** Where the desk stands in its room, in pixels from the room's corner; null when placed automatically. */
  spot?: { x: number; y: number } | null
}

/** A piece of furniture placed by hand, in pixels from the room's corner. */
export interface FurniturePiece {
  name: string
  x: number
  y: number
}

export type WallSide = 'Front' | 'Back' | 'Left' | 'Right'

/** A room's door: its wall and how many tiles from the start of it, left or back, it opens. */
export interface DoorPlacement {
  wall: WallSide
  offset: number
}

export interface RoomView {
  id: string
  name: string
  context: string
  procedure: string[]
  desks: DeskView[]
  /** Tiles from wall to wall and tiles of floor; null while the room is laid out automatically. */
  width?: number | null
  depth?: number | null
  furniture?: FurniturePiece[]
  /** Null for the middle of the front wall. */
  door?: DoorPlacement | null
  /** Windows of the back wall, by the pixel where each starts. */
  windows?: number[]
  /** The back left corner in tiles from the building's; null until the floor is drawn by hand. */
  left?: number | null
  top?: number | null
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
  /** The mood it is drawn with; unknown names fall back to the default. */
  theme: string
  rooms: RoomView[]
  /** The building's size in tiles; null while it is laid out automatically. */
  floorWidth?: number | null
  floorDepth?: number | null
  /** The common floor's furniture, in pixels from the building's corner. */
  furniture?: FurniturePiece[]
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

/** How a room is arranged by hand, as the API takes it. */
export interface RoomLayoutRequest {
  width: number
  depth: number
  desks: { deskId: string; x: number; y: number }[]
  furniture: FurniturePiece[]
  door: DoorPlacement
  windows: number[]
}

/** The building drawn by hand, as the API takes it. */
export interface FloorPlanRequest {
  width: number
  depth: number
  furniture: FurniturePiece[]
  rooms: { roomId: string; left: number; top: number; layout: RoomLayoutRequest }[]
}
