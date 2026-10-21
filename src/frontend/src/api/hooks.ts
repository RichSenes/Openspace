import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { request } from './client'
import type { DeskActivity, OfficeView, WorkTaskView } from './types'

export const keys = {
  offices: ['offices'] as const,
  office: (id: string) => ['offices', id] as const,
  tasks: ['tasks'] as const,
  officeTasks: (officeId: string) => ['tasks', 'office', officeId] as const,
  diff: (taskId: string) => ['tasks', 'diff', taskId] as const,
  activity: ['activity'] as const,
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
    (input: { roomId: string; name: string; agentName: string; model: string | null }) =>
      request<Created>('POST', `/api/offices/${officeId}/rooms/${input.roomId}/desks`, {
        name: input.name,
        agentName: input.agentName,
        model: input.model,
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
