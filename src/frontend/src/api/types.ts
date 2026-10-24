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
  /** How its agent looks, chosen by hand; null when picked from the desk's id. */
  look?: DeskLook | null
  /** What runs its agent; OpenCode when not said. */
  engine?: AgentEngine
}

export type AgentEngine = 'OpenCode' | 'ClaudeCode'

export interface ModelOption {
  id: string
  name: string
}

/** One window of the Claude account's limits: used is from 0 to 1. */
export interface UsageWindow {
  used: number
  resetsAt: string | null
}

/** How much of the Claude account's limits is used, as Claude Code last reported it. */
export interface ClaudeUsage {
  status: string
  fiveHours: UsageWindow | null
  week: UsageWindow | null
  seenAt: string
}

/** What can run agents on this machine, and with which models. */
export interface EnginesView {
  openCode: boolean
  openCodeModels: ModelOption[]
  claudeCode: boolean
  claudeCodeModels: ModelOption[]
  claudeUsage: ClaudeUsage | null
  supervisor: AgentEngine
  reviewer: AgentEngine | null
}

/** A look chosen by hand: names the pixel art knows; unknown ones fall back to the picked look. */
export interface DeskLook {
  skin: string
  hair: string
  hairStyle: string
  cloth: string
  accessory?: string | null
  outfit?: string | null
  costume?: string | null
}

/** A piece of furniture placed by hand, in pixels from the room's corner. */
export interface FurniturePiece {
  name: string
  x: number
  y: number
}

/** A piece cut out of the building's rectangle, in tiles from its corner: an L, T or U shape, or a courtyard. */
export interface FloorCut {
  left: number
  top: number
  width: number
  height: number
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
  /** A theme for this room only; null when it follows the office's. */
  mood?: string | null
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
  cuts?: FloorCut[]
}

export interface TaskNoteView {
  authorKind: 'Human' | 'Desk' | 'System' | 'Reviewer'
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
  /** When the task last changed status: a review counts only if written after it. */
  statusChangedAt: string
  /** 1, then one more each time the work is sent back. */
  round: number
  manualChecks: ManualCheckView[]
  usage: RoundUsageView[]
}

/** Something a person must try by hand before merging, and how. */
export interface ManualCheckView {
  number: number
  what: string
  how: string
  checkedAt: string | null
}

/** Tokens the agent spent on one round of a task. */
export interface RoundUsageView {
  round: number
  input: number
  output: number
  cacheRead: number
  cacheWrite: number
}

/** Tokens spent by one desk over all its tasks. */
export interface DeskUsageView {
  officeId: string
  officeName: string
  deskId: string
  deskName: string
  tasks: number
  input: number
  output: number
  cacheRead: number
  cacheWrite: number
}

export type Autonomy = 'Auto' | 'Ask'

/** What a person changes from Openspace itself; blank models mean the engine's default. */
export interface SettingsView {
  autonomy: Autonomy
  seats: number
  claudeSupervises: boolean
  supervisorModel: string | null
  claudeReviews: boolean
  reviewModel: string | null
  draftModel: string | null
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
  /** While waiting: exactly what is asked, such as the command. */
  detail?: string | null
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
  kind: 'Period' | 'Epic' | 'Changelog'
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
  cuts: FloorCut[]
}

export type MergeResult = 'Merged' | 'Conflict' | 'Failed' | 'NotTried'

/** What happened to one task when merging all accepted work. */
export interface MergeOutcome {
  taskId: string
  title: string
  result: MergeResult
  message: string | null
}
