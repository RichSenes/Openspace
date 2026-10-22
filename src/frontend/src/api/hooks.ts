import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { request } from './client'
import type {
  AuditEntryView,
  BranchList,
  DeskActivity,
  EpicView,
  OfficeView,
  PlanView,
  ReportView,
  RoleInput,
  RoleView,
  RoomLayoutRequest,
  WorkTaskView,
} from './types'

export const keys = {
  offices: ['offices'] as const,
  office: (id: string) => ['offices', id] as const,
  tasks: ['tasks'] as const,
  officeTasks: (officeId: string) => ['tasks', 'office', officeId] as const,
  diff: (taskId: string) => ['tasks', 'diff', taskId] as const,
  log: (taskId: string) => ['tasks', 'log', taskId] as const,
  activity: ['activity'] as const,
  roles: ['roles'] as const,
  epics: ['epics'] as const,
  officeEpics: (officeId: string) => ['epics', officeId] as const,
  reports: ['reports'] as const,
  officeReports: (officeId: string) => ['reports', officeId] as const,
  branches: (officeId: string) => ['offices', officeId, 'branches'] as const,
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
    (input: { roomId: string; name: string; agentName: string; model: string | null; roleId: string | null }) =>
      request<Created>('POST', `/api/offices/${officeId}/rooms/${input.roomId}/desks`, {
        name: input.name,
        agentName: input.agentName,
        model: input.model,
        roleId: input.roleId,
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

export type TaskCommand = 'accept' | 'retry' | 'discard'

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

export function useSetRepository(officeId: string) {
  return useWrite(
    (input: { path: string; initialize: boolean }) =>
      request<void>('PUT', `/api/offices/${officeId}/repository`, input),
    keys.offices,
  )
}

export function useSetOfficeTheme(officeId: string) {
  return useWrite((theme: string) => request<void>('PUT', `/api/offices/${officeId}/theme`, { theme }), keys.offices)
}

/** Saves how a room is arranged; 'auto' gives it back to the automatic layout. */
export function useArrangeRoom(officeId: string) {
  return useWrite(
    ({ roomId, layout }: { roomId: string; layout: RoomLayoutRequest | 'auto' }) =>
      layout === 'auto'
        ? request<void>('DELETE', `/api/offices/${officeId}/rooms/${roomId}/layout`)
        : request<void>('PUT', `/api/offices/${officeId}/rooms/${roomId}/layout`, layout),
    keys.offices,
  )
}

/** Problems with a repository folder that Openspace can fix by initializing git there. */
export const INITIALIZABLE = new Set(['not-a-repository', 'no-commits'])

/** Every desk's current activity. Kept fresh by SignalR, which writes into this cache. */
export function useActivity() {
  return useQuery({ queryKey: keys.activity, queryFn: () => request<DeskActivity[]>('GET', '/api/activity') })
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
