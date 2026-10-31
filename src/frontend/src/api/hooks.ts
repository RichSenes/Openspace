import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { readError, request } from './client'
import type {
  BranchMergeResult,
  DeskLook,
  AgentEngine,
  EnginesView,
  GpuView,
  KanjiCourseView,
  GoogleStatusView,
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
  PlanView,
  ReportView,
  RoleInput,
  RoleView,
  FloorPlanRequest,
  WorkTaskView,
  ClaudeUsage,
  DeskUsageView,
  SettingsView,
  MergeOutcome,
  CheckOutcome,
  BackupView,
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
  feed: ['feed'] as const,
  feedOf: (filter: FeedFilter) => ['feed', filter] as const,
  roles: ['roles'] as const,
  epics: ['epics'] as const,
  officeEpics: (officeId: string) => ['epics', officeId] as const,
  reports: ['reports'] as const,
  routines: ['routines'] as const,
  officeReports: (officeId: string) => ['reports', officeId] as const,
  branches: (officeId: string) => ['offices', officeId, 'branches'] as const,
  engines: ['engines'] as const,
  gpu: ['gpu'] as const,
  settings: ['settings'] as const,
  backups: ['backups'] as const,
  sounds: ['sounds'] as const,
  deskUsage: ['usage', 'desks'] as const,
  google: ['google'] as const,
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
  return useWrite(
    (input: { taskId: string; command: TaskCommand }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/${input.command}`),
    keys.tasks,
  )
}

export function useApproveTask() {
  return useWrite(
    (input: { taskId: string; deskId: string | null }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/approve`, { deskId: input.deskId }),
    keys.tasks,
  )
}

export function useTaskText(kind: 'notes' | 'send-back') {
  return useWrite(
    (input: { taskId: string; text: string }) =>
      request<void>('POST', `/api/tasks/${input.taskId}/${kind}`, { text: input.text }),
    keys.tasks,
  )
}

/** Sends work back with feedback, the reviewer's latest note, or both. */
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

export function useKeepLocal(officeId: string) {
  return useWrite(
    (localOnly: boolean) => request<void>('PUT', `/api/offices/${officeId}/local-only`, { localOnly }),
    keys.offices,
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

export function useSetRepository(officeId: string) {
  return useWrite(
    (input: { path: string; initialize: boolean }) =>
      request<void>('PUT', `/api/offices/${officeId}/repository`, input),
    keys.offices,
  )
}

/** Replaces the office's private folders: Openspace refuses every agent action that names them. */
/** Sets the office's reserve models; refused for an office kept on this machine. */
export function useSetReserveModels(officeId: string) {
  return useWrite(
    (models: string[]) => request<void>('PUT', `/api/offices/${officeId}/reserve-models`, { models }),
    keys.offices,
  )
}

export function useSetPrivatePaths(officeId: string) {
  return useWrite(
    (paths: string[]) => request<void>('PUT', `/api/offices/${officeId}/private-paths`, { paths }),
    keys.offices,
  )
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
      input.id
        ? request<void>('PUT', `/api/roles/${input.id}`, input.role).then(() => ({ id: input.id! }))
        : request<Created>('POST', '/api/roles', input.role),
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
export function usePlan(officeId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (text: string) => request<PlanView>('POST', `/api/offices/${officeId}/plan`, { request: text }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: keys.tasks })
      void client.invalidateQueries({ queryKey: keys.epics })
    },
  })
}

export function useApproveEpic() {
  return useWrite(
    (epicId: string) => request<{ approved: number }>('POST', `/api/epics/${epicId}/approve`, {}),
    keys.tasks,
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

export function useSettings() {
  return useQuery({ queryKey: keys.settings, queryFn: () => request<SettingsView>('GET', '/api/settings') })
}

/** Saves the settings; they apply at once, so engines are read again too. */
export function useSaveSettings() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (settings: SettingsView) => request<SettingsView>('PUT', '/api/settings', settings),
    onSuccess: (saved) => {
      client.setQueryData(keys.settings, saved)
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

export function useDecideProposal() {
  return useWrite(
    (input: { id: string; approve: boolean }) => request<void>('POST', `/api/proposals/${input.id}/${input.approve ? 'approve' : 'reject'}`),
    keys.routines,
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
