import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { readError, request } from './client'
import { notePath, withoutItem } from '../activity/needs'
import { pathsOf, type OfficeForm, type OfficePart } from '../office/officeForm'
import type {
  SecurityAuditProfile,
  SecurityAuditView,
  SupervisorJob,
  SecurityFindingView,
  SkillCatalogView,
  BranchMergeResult,
  DeskLook,
  AgentEngine,
  EnginesView,
  GpuView,
  KanjiCourseView,
  GoogleStatusView,
  OfficeSetup,
  TelegramChatView,
  TelegramStatusView,
  ProposalView,
  RoutineKindView,
  RoutineRunView,
  RoutineView,
  ScheduleView,
  AuditEntryView,
  BranchList,
  SoundsView,
  DeskActivity,
  DeskContext,
  DeskInterns,
  FeedLine,
  EpicView,
  OfficeView,
  ReportView,
  RoleInput,
  RoleView,
  FloorPlanRequest,
  WorkTaskView,
  ClaudeUsage,
  DeskUsageView,
  SettingsView,
  WeatherView,
  Guest,
  MergeOutcome,
  CheckOutcome,
  BackupView,
  QueueWait,
  VersionView,
  NeedsYouItem,
  BrainNoteView,
  DecisionVerb,
  LocalAiSettings,
  BrainSummary,
  LocalAiView,
  LocalModelSetting,
  ModelCheck,
  ModelsSettings,
  ModelsView,
  SettingsChange,
  ShioriEnding,
  ShioriMode,
  ShioriSessionSummary,
  ShioriSessionView,
  TranscriptEntry,
} from './types'

export const keys = {
  offices: ['offices'] as const,
  office: (id: string) => ['offices', id] as const,
  tasks: ['tasks'] as const,
  officeTasks: (officeId: string) => ['tasks', 'office', officeId] as const,
  diff: (taskId: string) => ['tasks', 'diff', taskId] as const,
  log: (taskId: string) => ['tasks', 'log', taskId] as const,
  activity: ['activity'] as const,
  context: ['context'] as const,
  interns: ['interns'] as const,
  guests: ['guests'] as const,
  queueWaits: ['queue-waits'] as const,
  version: ['version'] as const,
  feed: ['feed'] as const,
  feedOf: (filter: FeedFilter) => ['feed', filter] as const,
  roles: ['roles'] as const,
  epics: ['epics'] as const,
  officeEpics: (officeId: string) => ['epics', officeId] as const,
  reports: ['reports'] as const,
  routines: ['routines'] as const,
  needsYou: ['needs-you'] as const,
  officeReports: (officeId: string) => ['reports', officeId] as const,
  branches: (officeId: string) => ['offices', officeId, 'branches'] as const,
  engines: ['engines'] as const,
  gpu: ['gpu'] as const,
  localAi: ['local-ai'] as const,
  brainSummary: ['brain-summary'] as const,
  settings: ['settings'] as const,
  models: ['models'] as const,
  weather: ['weather'] as const,
  backups: ['backups'] as const,
  sounds: ['sounds'] as const,
  deskUsage: ['usage', 'desks'] as const,
  google: ['google'] as const,
  telegram: ['telegram'] as const,
  skills: ['skills'] as const,
  supervisorJobs: ['supervisor-jobs'] as const,
  securityAudits: (officeId: string) => ['security-audits', officeId] as const,
  securityFindings: (officeId: string, run: number) => ['security-audits', officeId, run, 'findings'] as const,
  securityReport: (officeId: string, run: number) => ['security-audits', officeId, run, 'report'] as const,
  shioriSessions: ['shiori', 'sessions'] as const,
  shioriSession: (id: string) => ['shiori', 'sessions', id] as const,
}

interface Created {
  id: string
}

export function useOffices() {
  return useQuery({ queryKey: keys.offices, queryFn: () => request<OfficeView[]>('GET', '/api/offices') })
}

export function useOfficeTasks(officeId: string | undefined) {
  return useQuery({
    queryKey: keys.officeTasks(officeId ?? ''),
    queryFn: () => request<WorkTaskView[]>('GET', `/api/tasks?officeId=${officeId}`),
    enabled: officeId !== undefined,
  })
}

/** Wraps a write: on success the affected data is refetched (SignalR does the same for other tabs). */
function useWrite<TInput, TResult>(fn: (input: TInput) => Promise<TResult>, invalidate: readonly unknown[]) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => client.invalidateQueries({ queryKey: invalidate }),
  })
}

/**
 * A write that settles something waiting for the person: its item leaves the "needs you" list at
 * once, and the list is read again when the write is done, without waiting for the hub's event.
 */
function useSettle<TInput, TResult>(fn: (input: TInput) => Promise<TResult>, invalidate: readonly unknown[], settled: (input: TInput) => string | null) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onMutate: async (input: TInput) => {
      const id = settled(input)
      if (!id) return undefined
      await client.cancelQueries({ queryKey: keys.needsYou })
      const before = client.getQueryData<NeedsYouItem[]>(keys.needsYou)
      client.setQueryData<NeedsYouItem[]>(keys.needsYou, (list) => withoutItem(list, id))
      return { before }
    },
    onError: (_error, _input, context) => {
      if (context?.before) client.setQueryData(keys.needsYou, context.before)
    },
    onSettled: () =>
      Promise.all([client.invalidateQueries({ queryKey: invalidate }), client.invalidateQueries({ queryKey: keys.needsYou })]),
  })
}

export function useOpenOffice() {
  return useWrite((name: string) => request<Created>('POST', '/api/offices', { name }), keys.offices)
}

export function useOpenRoom(officeId: string) {
  return useWrite(
    (name: string) => request<Created>('POST', `/api/offices/${officeId}/rooms`, { name }),
    keys.offices,
  )
}

export function useUpdateRoomContext(officeId: string) {
  return useWrite(
    (input: { roomId: string; context: string }) =>
      request<void>('PUT', `/api/offices/${officeId}/rooms/${input.roomId}/context`, { context: input.context }),
    keys.offices,
  )
}

export function useAddDesk(officeId: string) {
  return useWrite(
    (input: { roomId: string; name: string; agentName: string; model: string | null; roleId: string | null; engine: AgentEngine }) =>
      request<Created>('POST', `/api/offices/${officeId}/rooms/${input.roomId}/desks`, {
        name: input.name,
        agentName: input.agentName,
        model: input.model,
        roleId: input.roleId,
        engine: input.engine,
      }),
    keys.offices,
  )
}

export function useCreateTask() {
  return useWrite(
    (input: { officeId: string; deskId: string | null; title: string; description: string }) =>
      request<Created>('POST', '/api/tasks', input),
    keys.tasks,
  )
}

export type TaskCommand = 'accept' | 'retry' | 'stop' | 'discard'

export function useTaskCommand() {
  return useSettle(
    (input: { taskId: string; command: TaskCommand }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/${input.command}`),
    keys.tasks,
    (input) => (input.command === 'stop' ? null : input.taskId),
  )
}

export function useApproveTask() {
  return useSettle(
    (input: { taskId: string; deskId: string | null }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/approve`, { deskId: input.deskId }),
    keys.tasks,
    (input) => input.taskId,
  )
}

export function useTaskText(kind: 'notes' | 'send-back') {
  return useSettle(
    (input: { taskId: string; text: string }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/${kind}`, { text: input.text }),
    keys.tasks,
    (input) => (kind === 'send-back' ? input.taskId : null),
  )
}

/** Sends work back with feedback, the reviewer's latest note, or both. */
/** Asks the reviewer to read work in review again, such as after it could not run. */
export function useRequestReview() {
  return useWrite((taskId: string) => request<void>('POST', `/api/tasks/${taskId}/review`), keys.tasks)
}

export function useSendBack() {
  return useWrite(
    (input: { taskId: string; text: string | null; withReview: boolean }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/send-back`, { text: input.text, withReview: input.withReview }),
    keys.tasks,
  )
}

/** Ticks a check a person tried by hand, or puts it back on the list. */
export function useTickCheck() {
  return useWrite(
    (input: { taskId: string; number: number; done: boolean }) =>
      request<void>('PUT', `/api/tasks/${input.taskId}/checks/${input.number}`, { done: input.done }),
    keys.tasks,
  )
}

/** Sends one of your branches to its remote; never forced. */
export function usePush(officeId: string) {
  return useWrite(
    (branch: string) => request<{ to: string }>('POST', `/api/offices/${officeId}/push`, { branch }),
    keys.branches(officeId),
  )
}

/** Proposes accepted work in a pull request instead of merging it here. */
export function useOpenPullRequest() {
  return useWrite(
    (input: { taskId: string; target: string }) => request<{ url: string }>('POST', `/api/tasks/${input.taskId}/pull-request`, { target: input.target }),
    keys.tasks,
  )
}

/** Asks GitHub how a task's pull request stands: merged there, the work counts as merged. */
export function useCheckPullRequest() {
  return useWrite(
    (taskId: string) => request<{ state: 'Open' | 'Merged' | 'Closed' }>('POST', `/api/tasks/${taskId}/pull-request/check`),
    keys.tasks,
  )
}

/** Sets the tasks one waits for; refused when it would make a loop. */
export function useSetDependencies() {
  return useWrite(
    (input: { taskId: string; taskIds: string[] }) =>
      request<void>('PUT', `/api/tasks/${input.taskId}/dependencies`, { taskIds: input.taskIds }),
    keys.tasks,
  )
}

/** A part of the office's settings that could not be saved, with the server's reason. */
export interface OfficeSaveFailure {
  part: OfficePart
  error: Error
}

/**
 * Saves the parts of the office's settings that changed, one after the other: a refused
 * repository does not keep the rest from being saved. It gives back what failed.
 */
export function useSaveOffice(officeId: string) {
  const client = useQueryClient()
  const base = `/api/offices/${officeId}`
  const save: Record<OfficePart, (form: OfficeForm, initialize: boolean) => Promise<void>> = {
    repository: (f, initialize) => request<void>('PUT', `${base}/repository`, { path: f.repositoryPath.trim(), initialize }),
    name: (f) => request<void>('PUT', `${base}/name`, { name: f.name }),
    localOnly: (f) => request<void>('PUT', `${base}/local-only`, { localOnly: f.localOnly }),
    holdOverlaps: (f) => request<void>('PUT', `${base}/hold-overlaps`, { hold: f.holdOverlaps }),
    privatePaths: (f) => request<void>('PUT', `${base}/private-paths`, { paths: pathsOf(f.privatePaths) }),
    reserveModels: (f) => request<void>('PUT', `${base}/reserve-models`, { models: f.reserveModels }),
  }
  return useMutation({
    mutationFn: async (input: { form: OfficeForm; parts: OfficePart[]; initialize?: boolean }) => {
      const failed: OfficeSaveFailure[] = []
      for (const part of input.parts) {
        try {
          await save[part](input.form, input.initialize ?? false)
        } catch (error) {
          failed.push({ part, error: error as Error })
        }
      }
      return failed
    },
    onSettled: () => client.invalidateQueries({ queryKey: keys.offices }),
  })
}

export function useSetOfficeTheme(officeId: string) {
  return useWrite((theme: string) => request<void>('PUT', `/api/offices/${officeId}/theme`, { theme }), keys.offices)
}

/** Saves the building drawn by hand; 'auto' gives the whole office back to the automatic layout. */
export function useSaveFloorPlan(officeId: string) {
  return useWrite(
    (plan: FloorPlanRequest | 'auto') =>
      plan === 'auto' ? request<void>('DELETE', `/api/offices/${officeId}/plan`) : request<void>('PUT', `/api/offices/${officeId}/plan`, plan),
    keys.offices,
  )
}

/** Problems with a repository folder that Openspace can fix by initializing git there. */
export const INITIALIZABLE = new Set(['not-a-repository', 'no-commits'])

/** Every desk's current activity. Kept fresh by SignalR, which writes into this cache. */
export function useActivity() {
  return useQuery({ queryKey: keys.activity, queryFn: () => request<DeskActivity[]>('GET', '/api/activity') })
}

/** Which part of the building the feed follows. */
export interface FeedFilter {
  /** Null for every office. */
  officeId: string | null
  roomId: string | null
  /** Reads and searches too. */
  everything: boolean
  limit: number
}

/** The latest things that happened, newest first. Refetched when a task or its log changes. */
export function useFeed(filter: FeedFilter) {
  const query = new URLSearchParams({ everything: String(filter.everything), limit: String(filter.limit) })
  if (filter.officeId) query.set('officeId', filter.officeId)
  if (filter.officeId && filter.roomId) query.set('roomId', filter.roomId)
  return useQuery({
    queryKey: keys.feedOf(filter),
    queryFn: () => request<FeedLine[]>('GET', `/api/feed?${query}`),
    placeholderData: (previous) => previous,
  })
}

/** Why each queued task waits, by task id. Kept fresh by SignalR, which writes into this cache. */
export function useQueueWaits() {
  return useQuery({
    queryKey: keys.queueWaits,
    queryFn: () => request<QueueWait[]>('GET', '/api/queue/waits'),
    select: (waits) => new Map(waits.map((w) => [w.taskId, w.reason])),
  })
}

/** The guests of every office. Kept fresh by SignalR, which writes into this cache. */
export function useGuests() {
  return useQuery({ queryKey: keys.guests, queryFn: () => request<Guest[]>('GET', '/api/guests') })
}

/** Every desk's interns. Kept fresh by SignalR, which writes into this cache. */
export function useInterns() {
  return useQuery({ queryKey: keys.interns, queryFn: () => request<DeskInterns[]>('GET', '/api/interns') })
}

/** Every desk's energy. Kept fresh by SignalR, which writes into this cache. */
export function useDeskContexts() {
  return useQuery({ queryKey: keys.context, queryFn: () => request<DeskContext[]>('GET', '/api/context') })
}

export function useTaskDiff(taskId: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.diff(taskId),
    queryFn: async () => {
      const response = await fetch(`/api/tasks/${taskId}/diff`)
      return response.ok ? response.text() : ''
    },
    enabled,
  })
}

export function useAnswerPermission() {
  return useWrite(
    (input: { taskId: string; allow: boolean }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/permission`, { allow: input.allow }),
    keys.activity,
  )
}

export function useBranches(officeId: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.branches(officeId),
    queryFn: () => request<BranchList>('GET', `/api/offices/${officeId}/branches`),
    enabled,
  })
}

export function useMergeTask() {
  return useWrite(
    (input: { taskId: string; target: string }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/merge`, { target: input.target }),
    keys.tasks,
  )
}

/** Gives a room its own mood; null follows the office's theme again. */
export function useChangeRoomMood(officeId: string) {
  return useWrite(
    (input: { roomId: string; mood: string | null }) =>
      request<void>('PUT', `/api/offices/${officeId}/rooms/${input.roomId}/mood`, { mood: input.mood }),
    keys.offices,
  )
}

/** Brings all accepted work, or the chosen tasks, into one branch in the order it was accepted. */
export function useMergeAll(officeId: string) {
  return useWrite(
    (input: { target: string; taskIds: string[] }) =>
      request<MergeOutcome[]>('POST', `/api/offices/${officeId}/merge`, input),
    keys.tasks,
  )
}

/** Brings one of the office's branches into another, such as beta into main; the source stays. */
export function useMergeBranch(officeId: string) {
  return useWrite(
    (input: { source: string; target: string }) =>
      request<{ result: BranchMergeResult }>('POST', `/api/offices/${officeId}/merge-branch`, input),
    keys.branches(officeId),
  )
}

/** Sends accepted work that conflicts back to its agent, with the merge already started. */
export function useCatchUp() {
  return useWrite(
    (input: { taskId: string; target: string }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/catch-up`, { target: input.target }),
    keys.tasks,
  )
}

/** A task's audit log. Refetched when the task changes or its log grows. */
export function useTaskLog(taskId: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.log(taskId),
    queryFn: () => request<AuditEntryView[]>('GET', `/api/tasks/${taskId}/log`),
    enabled,
  })
}

export function useRoles() {
  return useQuery({ queryKey: keys.roles, queryFn: () => request<RoleView[]>('GET', '/api/roles') })
}

export function useSaveRole() {
  return useWrite(
    (input: { id: string | null; role: RoleInput }) =>
      (input.id
        ? request<void>('PUT', `/api/roles/${input.id}`, input.role).then(() => ({ id: input.id! }))
        : request<Created>('POST', '/api/roles', input.role)
      ).then(async ({ id }) => {
        await request<void>('PUT', `/api/roles/${id}/skills`, { skills: input.role.skills })
        return { id }
      }),
    keys.roles,
  )
}

export function useDeleteRole() {
  return useWrite((id: string) => request<void>('DELETE', `/api/roles/${id}`), keys.roles)
}

/** Asks the AI for role instructions; nothing is saved until the person uses the draft. */
export function useDraftRole() {
  return useMutation({
    mutationFn: (input: { name: string; description: string }) =>
      request<{ instructions: string }>('POST', '/api/roles/draft', input).then((r) => r.instructions),
  })
}

export function useSetDeskRole(officeId: string) {
  return useWrite(
    (input: { deskId: string; roleId: string | null }) =>
      request<void>('PUT', `/api/offices/${officeId}/desks/${input.deskId}/role`, { roleId: input.roleId }),
    keys.offices,
  )
}

export function useEpics(officeId: string) {
  return useQuery({
    queryKey: keys.officeEpics(officeId),
    queryFn: () => request<EpicView[]>('GET', `/api/epics?officeId=${officeId}`),
  })
}

/** Asks the supervisor to split a request into proposed tasks. Can take a minute. */
/** Asks the supervisor for a plan: it runs in the background and shows up in the supervisor's jobs. */
export function usePlan(officeId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (text: string) => request<SupervisorJob>('POST', `/api/offices/${officeId}/plan`, { request: text }),
    onSuccess: (job) => client.setQueryData<SupervisorJob[]>(keys.supervisorJobs, (list) => mergeJob(list, job)),
  })
}

/** The supervisor's plans of every office: running, and finished in the last quarter of an hour. */
export function useSupervisorJobs() {
  return useQuery({ queryKey: keys.supervisorJobs, queryFn: () => request<SupervisorJob[]>('GET', '/api/supervisor/jobs') })
}

function mergeJob(list: SupervisorJob[] | undefined, job: SupervisorJob): SupervisorJob[] {
  return [...(list ?? []).filter((j) => j.id !== job.id), job]
}

export function useApproveEpic() {
  return useSettle(
    (epicId: string) => request<{ approved: number }>('POST', `/api/epics/${epicId}/approve`, {}),
    keys.tasks,
    () => null,
  )
}

/** What can run agents here; refreshed every minute, for the Claude account's usage. */
export function useEngines() {
  return useQuery({
    queryKey: keys.engines,
    queryFn: () => request<EnginesView>('GET', '/api/engines'),
    refetchInterval: 60_000,
  })
}

/** Who has the graphics card: it changes within seconds when a program takes it. */
/** Where the brain's vault is and how many notes of each kind it holds. */
export function useBrainSummary() {
  return useQuery({ queryKey: keys.brainSummary, queryFn: () => request<BrainSummary>('GET', '/api/brain/summary') })
}

/** Keeps a new folder for the vault; Openspace opens it at its next start. */
export function useMoveBrain() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (folder: string) => request<BrainSummary>('PUT', '/api/brain/folder', { folder }),
    onSuccess: (summary) => client.setQueryData(keys.brainSummary, summary),
  })
}

/** The local models' settings, with the card's memory and the programs that hold it now. */
export function useLocalAi() {
  return useQuery({ queryKey: keys.localAi, queryFn: () => request<LocalAiView>('GET', '/api/local-ai') })
}

/** Saves the local models' settings; they apply at once and the view comes back. */
export function useSaveLocalAi() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (settings: LocalAiSettings) => request<LocalAiView>('PUT', '/api/local-ai', settings),
    onSuccess: (view) => {
      client.setQueryData(keys.localAi, view)
      // Settings › Models chooses among the local models.
      void client.invalidateQueries({ queryKey: keys.models })
      void client.invalidateQueries({ queryKey: keys.gpu })
      void client.invalidateQueries({ queryKey: keys.engines })
    },
  })
}

/** Looks at one model without loading it: its command, its files, whether it fits the card. */
export function useCheckLocalModel() {
  return useMutation({ mutationFn: (model: LocalModelSetting) => request<ModelCheck>('POST', '/api/local-ai/check', model) })
}

export function useGpu() {
  return useQuery({
    queryKey: keys.gpu,
    queryFn: () => request<GpuView>('GET', '/api/gpu'),
    refetchInterval: 5_000,
  })
}

/** After the model failed to load: try again now instead of waiting. */
export function useRetryGpu() {
  return useWrite(() => request<void>('POST', '/api/gpu/retry'), keys.gpu)
}

/** Changes what runs a desk's agent: engine, agent and model. */
export function useChangeDeskAgent(officeId: string) {
  return useWrite(
    (input: { deskId: string; engine: AgentEngine; agentName: string; model: string | null }) =>
      request<void>('PUT', `/api/offices/${officeId}/desks/${input.deskId}/agent`, {
        engine: input.engine,
        agentName: input.agentName,
        model: input.model,
      }),
    keys.offices,
  )
}

/** Dresses a desk's agent; null gives it back the look picked from its id. */
export function useDressDesk(officeId: string) {
  return useWrite(
    (input: { deskId: string; look: DeskLook | null }) =>
      input.look
        ? request<void>('PUT', `/api/offices/${officeId}/desks/${input.deskId}/look`, input.look)
        : request<void>('DELETE', `/api/offices/${officeId}/desks/${input.deskId}/look`),
    keys.offices,
  )
}

export function useRenameDesk(officeId: string) {
  return useWrite(
    (input: { deskId: string; name: string }) =>
      request<void>('PUT', `/api/offices/${officeId}/desks/${input.deskId}/name`, { name: input.name }),
    keys.offices,
  )
}

export function useSetRoomProcedure(officeId: string) {
  return useWrite(
    (input: { roomId: string; steps: string[] }) =>
      request<void>('PUT', `/api/offices/${officeId}/rooms/${input.roomId}/procedure`, { steps: input.steps }),
    keys.offices,
  )
}

export function useSetRoomCheck(officeId: string) {
  return useWrite(
    (input: { roomId: string; command: string | null }) =>
      request<void>('PUT', `/api/offices/${officeId}/rooms/${input.roomId}/check`, { command: input.command }),
    keys.offices,
  )
}

/** Runs the rooms' checks on a branch: only the rooms of the given tasks, or all of them. */
export function useRunChecks(officeId: string) {
  return useWrite(
    (input: { branch: string; taskIds: string[] | null }) => request<CheckOutcome[]>('POST', `/api/offices/${officeId}/checks`, input),
    keys.tasks,
  )
}

export function useReports(officeId: string) {
  return useQuery({
    queryKey: keys.officeReports(officeId),
    queryFn: () => request<ReportView[]>('GET', `/api/offices/${officeId}/reports`),
  })
}

/** Writes a report of a day or of an epic. The AI part can take a minute. */
export function useWriteReport(officeId: string) {
  return useWrite(
    (input: { kind: 'day'; from: string; to: string; scope: string; language: string } | { kind: 'epic'; epicId: string; language: string }) =>
      input.kind === 'day'
        ? request<ReportView>('POST', `/api/offices/${officeId}/reports`, input)
        : request<ReportView>('POST', `/api/epics/${input.epicId}/report`, { language: input.language }),
    keys.reports,
  )
}

/** The changelog of what was accepted since the previous one, for people using the product. */
export function useWriteChangelog(officeId: string) {
  return useWrite(
    (input: { language: string }) => request<ReportView>('POST', `/api/offices/${officeId}/changelog`, input),
    keys.reports,
  )
}

export function useSounds() {
  return useQuery({ queryKey: keys.sounds, queryFn: () => request<SoundsView>('GET', '/api/sounds') })
}

/** Saves the sound choices; the page plays with them at once. */
export function useSaveSounds() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (config: Pick<SoundsView, 'muted' | 'volume' | 'rules'>) => request<SoundsView>('PUT', '/api/sounds', config),
    onSuccess: (saved) => client.setQueryData(keys.sounds, saved),
  })
}

/** Keeps a sound file of yours on this machine. */
export function useUploadSound() {
  return useWrite(async (file: File) => {
    const form = new FormData()
    form.append('file', file)
    const response = await fetch('/api/sounds/files', { method: 'POST', body: form })
    if (!response.ok) throw await readError(response)
    return (await response.json()) as { name: string }
  }, keys.sounds)
}

export function useDeleteSound() {
  return useWrite((name: string) => request<void>('DELETE', `/api/sounds/files/${encodeURIComponent(name)}`), keys.sounds)
}

export function useBackups() {
  return useQuery({ queryKey: keys.backups, queryFn: () => request<BackupView[]>('GET', '/api/backups') })
}

export function useBackUp() {
  return useWrite(() => request<BackupView>('POST', '/api/backups'), keys.backups)
}

/** The weather at the windows: the API asks again every quarter of an hour, so does the page. */
export function useWeather() {
  return useQuery({ queryKey: keys.weather, queryFn: () => request<WeatherView>('GET', '/api/weather'), refetchInterval: 15 * 60_000 })
}

/** The version running here and the latest release seen, asked again every hour. */
export function useVersion() {
  return useQuery({ queryKey: keys.version, queryFn: () => request<VersionView>('GET', '/api/version'), refetchInterval: 60 * 60_000 })
}

/** Looks for a new release now. */
export function useCheckVersion() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => request<VersionView>('POST', '/api/version/check'),
    onSuccess: (view) => client.setQueryData(keys.version, view),
  })
}

/** Sets the town of the weather; empty stops it. */
export function useSaveWeather() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (place: string) => request<WeatherView>('PUT', '/api/weather', { place }),
    onSuccess: (saved) => client.setQueryData(keys.weather, saved),
  })
}

export function useSettings() {
  return useQuery({ queryKey: keys.settings, queryFn: () => request<SettingsView>('GET', '/api/settings') })
}

/** Saves the settings; they apply at once, so engines are read again too. */
export function useSaveSettings() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (settings: SettingsChange) => request<SettingsView>('PUT', '/api/settings', settings),
    onSuccess: (saved) => {
      client.setQueryData(keys.settings, saved)
      void client.invalidateQueries({ queryKey: keys.engines })
    },
  })
}

/** Each function's engine and model, with the local models to choose from. */
export function useModels() {
  return useQuery({ queryKey: keys.models, queryFn: () => request<ModelsView>('GET', '/api/models') })
}

/** Saves Settings › Models; who plans and reviews changes too, so settings and engines are read again. */
export function useSaveModels() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (models: ModelsSettings) => request<ModelsView>('PUT', '/api/models', models),
    onSuccess: (view) => {
      client.setQueryData(keys.models, view)
      void client.invalidateQueries({ queryKey: keys.settings })
      void client.invalidateQueries({ queryKey: keys.engines })
    },
  })
}

/** Asks Claude Code now how much of the account is used: a tiny question to the smallest model. */
export function useCheckClaudeUsage() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => request<ClaudeUsage>('POST', '/api/engines/claude-usage'),
    onSuccess: (usage) => client.setQueryData<EnginesView>(keys.engines, (e) => (e ? { ...e, claudeUsage: usage } : e)),
  })
}

/** Tokens spent desk by desk; refreshed with the tasks. */
export function useDeskUsage() {
  return useQuery({ queryKey: keys.deskUsage, queryFn: () => request<DeskUsageView[]>('GET', '/api/usage/desks') })
}

export function useRoutineKinds() {
  return useQuery({ queryKey: [...keys.routines, 'kinds'], queryFn: () => request<RoutineKindView[]>('GET', '/api/routine-kinds') })
}

export function useRoutines(officeId: string) {
  return useQuery({
    queryKey: [...keys.routines, officeId],
    queryFn: () => request<RoutineView[]>('GET', `/api/offices/${officeId}/routines`),
    // Next and last runs move with time even when nothing is said over the hub.
    refetchInterval: 60_000,
  })
}

export function useRoutineRuns(routineId: string | undefined) {
  return useQuery({
    queryKey: [...keys.routines, 'runs', routineId],
    queryFn: () => request<RoutineRunView[]>('GET', `/api/routines/${routineId}/runs`),
    enabled: Boolean(routineId),
  })
}

export function useProposals(officeId: string) {
  return useQuery({
    queryKey: [...keys.routines, 'proposals', officeId],
    queryFn: () => request<ProposalView[]>('GET', `/api/offices/${officeId}/proposals?status=Pending`),
  })
}

export interface RoutineInput {
  name: string
  kind: string
  schedule: ScheduleView
  settings: string
}

export function useCreateRoutine(officeId: string) {
  return useWrite((input: RoutineInput) => request<Created>('POST', `/api/offices/${officeId}/routines`, input), keys.routines)
}

export function useUpdateRoutine() {
  return useWrite(
    (input: { id: string; name?: string; schedule?: ScheduleView; settings?: string }) =>
      request<void>('PUT', `/api/routines/${input.id}`, { name: input.name, schedule: input.schedule, settings: input.settings }),
    keys.routines,
  )
}

export function useTurnOnRoutine() {
  return useWrite(
    (input: { id: string; enabled: boolean }) => request<void>('PUT', `/api/routines/${input.id}/enabled`, { enabled: input.enabled }),
    keys.routines,
  )
}

export function useKanjiCourse(routineId: string, enabled: boolean) {
  return useQuery({
    queryKey: [...keys.routines, 'kanji', routineId],
    queryFn: () => request<KanjiCourseView>('GET', `/api/routines/${routineId}/kanji`),
    enabled,
  })
}

/** The person already knows this kanji: the paper never explains it. */
export function useKnownKanji() {
  return useWrite(
    (input: { id: string; kanji: string }) => request<void>('POST', `/api/routines/${input.id}/kanji/known`, { kanji: input.kanji }),
    keys.routines,
  )
}

export function useAskFirst() {
  return useWrite(
    (input: { id: string; askFirst: boolean }) => request<void>('PUT', `/api/routines/${input.id}/ask-first`, { askFirst: input.askFirst }),
    keys.routines,
  )
}

export function useStopRoutine() {
  return useWrite((id: string) => request<void>('POST', `/api/routines/${id}/stop`), keys.routines)
}

export function useDeleteRoutine() {
  return useWrite((id: string) => request<void>('DELETE', `/api/routines/${id}`), keys.routines)
}

/** An offer the person found and pastes in, for the job offers routine to judge now. */
export function useBringOffer() {
  return useWrite(
    (input: { id: string; text: string; link: string }) =>
      request<void>('POST', `/api/routines/${input.id}/offers`, { text: input.text.trim(), link: input.link.trim() || null }),
    keys.routines,
  )
}

export function useRunRoutine() {
  return useWrite((id: string) => request<void>('POST', `/api/routines/${id}/run`), keys.routines)
}

/** Everything that waits for the person, from every office; the hub refreshes it as tasks and routines change. */
export function useNeedsYou() {
  return useQuery({
    queryKey: keys.needsYou,
    queryFn: () => request<NeedsYouItem[]>('GET', '/api/needs-you'),
    refetchInterval: 60_000,
  })
}

/** The person's answer on a decision of the brain; the item leaves the list at once. */
export function useAnswerDecision() {
  return useSettle(
    (input: { id: string; path: string; verb: DecisionVerb }) =>
      request<BrainNoteView>('POST', `/api/brain/decisions/${notePath(input.path)}/${input.verb}`),
    keys.needsYou,
    (input) => input.id,
  )
}

/** One note of the brain, read only when asked for. */
export function useBrainNote(path: string | null) {
  return useQuery({
    queryKey: ['brain-note', path],
    queryFn: () => request<BrainNoteView>('GET', `/api/brain/notes/${notePath(path ?? '')}`),
    enabled: path !== null,
  })
}

export function useDecideProposal() {
  return useSettle(
    (input: { id: string; approve: boolean }) => request<void>('POST', `/api/proposals/${input.id}/${input.approve ? 'approve' : 'reject'}`),
    keys.routines,
    (input) => input.id,
  )
}

/** The Google accounts; while the person is saying yes in another tab, it looks again every few seconds. */
export function useGoogle(waiting = false) {
  return useQuery({
    queryKey: keys.google,
    queryFn: () => request<GoogleStatusView>('GET', '/api/google'),
    refetchInterval: waiting ? 3000 : false,
  })
}

/** Where to send the person to connect an account: Google's own page, in a new tab. */
export function useConnectGoogle() {
  return useWrite(() => request<{ url: string }>('POST', '/api/google/connect'), keys.google)
}

export function useDisconnectGoogle() {
  return useWrite((email: string) => request<void>('DELETE', `/api/google/accounts/${encodeURIComponent(email)}`), keys.google)
}

/** Keeps the OAuth client file the person chose; its text goes to this computer's API and nowhere else. */
export function useSaveGoogleClient() {
  return useWrite((json: string) => request<void>('PUT', '/api/google/client', { json }), keys.google)
}

/** Asks Google for a fresh token for the account: it says whether the account still works. */
export function useCheckGoogle() {
  return useMutation({ mutationFn: (email: string) => request<void>('POST', `/api/google/accounts/${encodeURIComponent(email)}/check`) })
}

/** The person's Telegram bots and group, by name: never a token. */
export function useTelegram() {
  return useQuery({ queryKey: keys.telegram, queryFn: () => request<TelegramStatusView>('GET', '/api/telegram') })
}

/** A Telegram change also changes the channels routines can choose from. */
function useTelegramWrite<TInput, TResult>(fn: (input: TInput) => Promise<TResult>) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () =>
      Promise.all([client.invalidateQueries({ queryKey: keys.telegram }), client.invalidateQueries({ queryKey: [...keys.routines, 'channels'] })]),
  })
}

export function useAddTelegramBot() {
  return useTelegramWrite((input: { name: string; token: string; topic: number | null }) => request<{ username: string }>('POST', '/api/telegram/bots', input))
}

export function useRemoveTelegramBot() {
  return useTelegramWrite((name: string) => request<void>('DELETE', `/api/telegram/bots/${encodeURIComponent(name)}`))
}

export function useSetTelegramChat() {
  return useTelegramWrite((chat: number) => request<void>('PUT', '/api/telegram/chat', { chat }))
}

export function useSetTelegramTopic() {
  return useTelegramWrite((input: { name: string; topic: number | null }) =>
    request<void>('PUT', `/api/telegram/bots/${encodeURIComponent(input.name)}/topic`, { topic: input.topic }),
  )
}

/** The chats a bot was added to or written in lately: asked for when the person presses the button. */
export function useFindTelegramChats() {
  return useMutation({ mutationFn: (name: string) => request<TelegramChatView[]>('GET', `/api/telegram/bots/${encodeURIComponent(name)}/chats`) })
}

export function useTestTelegramBot() {
  return useMutation({ mutationFn: (name: string) => request<void>('POST', `/api/telegram/bots/${encodeURIComponent(name)}/test`) })
}

/** An office's setup, to save as a file. */
export function exportOfficeSetup(officeId: string) {
  return request<OfficeSetup>('GET', `/api/offices/${officeId}/setup`)
}

/** Opens a new office from a setup: a template's or a file's. Roles may be added, so they are read again too. */
export function useImportOffice() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { setup: OfficeSetup; name: string }) => request<Created>('POST', '/api/offices/import', input),
    onSuccess: () =>
      Promise.all([
        client.invalidateQueries({ queryKey: keys.offices }),
        client.invalidateQueries({ queryKey: keys.roles }),
        client.invalidateQueries({ queryKey: keys.routines }),
      ]),
  })
}

/** The skills installed for the agents, to choose from in a role. */
export function useSkills() {
  return useQuery({ queryKey: keys.skills, queryFn: () => request<SkillCatalogView>('GET', '/api/skills') })
}

/** The office's security audits, newest first; polled while one is running. */
export function useSecurityAudits(officeId: string) {
  return useQuery({
    queryKey: keys.securityAudits(officeId),
    queryFn: () => request<SecurityAuditView[]>('GET', `/api/offices/${officeId}/security-audits`),
    refetchInterval: (query) => (query.state.data?.some((a) => a.status === 'Running' || a.status === 'Queued') ? 15_000 : false),
  })
}

export function useSecurityFindings(officeId: string, run: number | undefined) {
  return useQuery({
    queryKey: keys.securityFindings(officeId, run ?? 0),
    queryFn: () => request<SecurityFindingView[]>('GET', `/api/offices/${officeId}/security-audits/${run}/findings`),
    enabled: run !== undefined,
  })
}

/** The run's REPORT.md, or null while it is not written yet. */
export function useSecurityReport(officeId: string, run: number | undefined, ready: boolean) {
  return useQuery({
    queryKey: keys.securityReport(officeId, run ?? 0),
    queryFn: async () => {
      const response = await fetch(`/api/offices/${officeId}/security-audits/${run}/report`)
      if (response.status === 404) return null
      if (!response.ok) throw await readError(response)
      return response.text()
    },
    enabled: run !== undefined && ready,
  })
}

export function useAddSecurityDesk(officeId: string) {
  return useWrite(
    (model: string | null) => request<{ deskId: string }>('POST', `/api/offices/${officeId}/security-desk`, { model }),
    keys.offices,
  )
}

export function useStartSecurityAudit(officeId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { profile: SecurityAuditProfile; language: string; handOff: boolean }) =>
      request<{ taskId: string }>('POST', `/api/offices/${officeId}/security-audits`, input),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: keys.securityAudits(officeId) })
      void client.invalidateQueries({ queryKey: keys.tasks })
    },
  })
}

/** Hands findings to the office's supervisor in the background; its plan shows up in the supervisor's jobs. */
function useHandOffWrite<TInput>(url: (input: TInput) => string, body: (input: TInput) => unknown) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: TInput) => request<SupervisorJob>('POST', url(input), body(input)),
    onSuccess: (job) => client.setQueryData<SupervisorJob[]>(keys.supervisorJobs, (list) => mergeJob(list, job)),
  })
}

/** One finding to the supervisor. */
export function useProposeFix(officeId: string) {
  return useHandOffWrite(
    (input: { run: number; fingerprint: string; language: string }) =>
      `/api/offices/${officeId}/security-audits/${input.run}/findings/${encodeURIComponent(input.fingerprint)}/fix`,
    (input) => ({ language: input.language }),
  )
}

/** Every open finding of a run to the supervisor. */
export function useHandOff(officeId: string) {
  return useHandOffWrite(
    (input: { run: number; language: string }) => `/api/offices/${officeId}/security-audits/${input.run}/hand-off`,
    (input) => ({ language: input.language }),
  )
}

/** The channels a routine may deliver to: names of the bots, never their keys. */
export function useRoutineChannels() {
  return useQuery({ queryKey: [...keys.routines, 'channels'], queryFn: () => request<string[]>('GET', '/api/routine-channels') })
}

/** The local models routines may write with, as "local/<id>". */
export function useRoutineModels() {
  return useQuery({ queryKey: [...keys.routines, 'models'], queryFn: () => request<{ id: string; name: string }[]>('GET', '/api/routine-models') })
}

export interface FeedTest {
  ok: boolean
  count: number
  titles: string[]
  problem: string | null
}

/** Reads a news feed once and tells what came from it. */
/** A job board read once, as the offers routine would: how many offers, the newest. */
export function useTestBoard() {
  return useMutation({ mutationFn: (board: string) => request<FeedTest>('POST', '/api/job-boards/test', { board }) })
}

export function useTestFeed() {
  return useMutation({ mutationFn: (url: string) => request<FeedTest>('POST', '/api/feeds/test', { url }) })
}

/** The paper laid out as these settings say, with sample text: a page of HTML. */
export function usePaperPreview(settings: string, enabled: boolean) {
  return useQuery({
    queryKey: [...keys.routines, 'paper-preview', settings],
    queryFn: async () => {
      const response = await fetch('/api/routine-kinds/morning-paper/preview', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ settings }),
      })
      if (!response.ok) throw await readError(response)
      return response.text()
    },
    enabled,
    placeholderData: (previous) => previous,
    staleTime: Infinity,
  })
}

export function useShioriSessions() {
  return useQuery({ queryKey: keys.shioriSessions, queryFn: () => request<ShioriSessionSummary[]>('GET', '/api/shiori/sessions') })
}

export function useShioriSession(id: string | undefined) {
  return useQuery({
    queryKey: keys.shioriSession(id ?? ''),
    queryFn: () => request<ShioriSessionView>('GET', `/api/shiori/sessions/${id}`),
    enabled: !!id,
    // The hub tells the answer as it is written; reading again now and then covers a lost connection.
    refetchInterval: (query) => (query.state.data?.answering ? 5000 : false),
  })
}

/** Writes to a session put what the server answers into the cache at once; the hub tells the rest. */
function useShioriWrite<TInput, TResult>(fn: (input: TInput) => Promise<TResult>, view: (result: TResult, input: TInput) => ShioriSessionView | null) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: (result, input) => {
      const v = view(result, input)
      if (v) client.setQueryData(keys.shioriSession(v.id), v)
      return client.invalidateQueries({ queryKey: keys.shioriSessions, exact: true })
    },
  })
}

export function useCreateShioriSession() {
  return useShioriWrite(
    (input: { title: string | null; mode: ShioriMode }) => request<ShioriSessionView>('POST', '/api/shiori/sessions', input),
    (v) => v,
  )
}

export function useSendShioriMessage() {
  return useMutation({
    mutationFn: (input: { sessionId: string; text: string }) =>
      request<TranscriptEntry>('POST', `/api/shiori/sessions/${input.sessionId}/messages`, { text: input.text }),
  })
}

export function useSetShioriMode() {
  return useShioriWrite(
    (input: { sessionId: string; mode: ShioriMode }) => request<ShioriSessionView>('PUT', `/api/shiori/sessions/${input.sessionId}/mode`, { mode: input.mode }),
    (v) => v,
  )
}

export function useEndShioriSession() {
  return useShioriWrite((sessionId: string) => request<ShioriEnding>('POST', `/api/shiori/sessions/${sessionId}/end`), (e) => e.session)
}
