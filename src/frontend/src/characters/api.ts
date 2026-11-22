import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { request } from '../api/client'
import type { AgendaView, CharacterBoardView, Shift, TrustedAction, TrustLevel } from './characters'

const key = (officeId: string) => ['characters', officeId] as const

/** The character monitor of an office: read again when work changes, since moods follow it. */
export function useCharacters(officeId: string) {
  return useQuery({
    queryKey: key(officeId),
    queryFn: () => request<CharacterBoardView>('GET', `/api/offices/${officeId}/characters`),
    refetchInterval: 60_000,
  })
}

/** The office's next days: queued work, routines, reviews and, when connected, the calendar. */
export function useAgenda(officeId: string) {
  return useQuery({
    queryKey: ['agenda', officeId],
    queryFn: () => request<AgendaView>('GET', `/api/offices/${officeId}/agenda`),
    refetchInterval: 5 * 60_000,
  })
}

type DeskChange =
  | { deskId: string; kind: 'shift'; shift: Shift }
  | { deskId: string; kind: 'trust'; action: TrustedAction; level: TrustLevel }
  | { deskId: string; kind: 'birthday'; month: number; day: number }
  | { deskId: string; kind: 'birthday-reset' }

/** Changes what the person sets on a desk: its shift, its trust, its birthday. */
export function useChangeDesk(officeId: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (change: DeskChange) => {
      const base = `/api/offices/${officeId}/desks/${change.deskId}`
      switch (change.kind) {
        case 'shift':
          return request<void>('PUT', `${base}/shift`, { shift: change.shift })
        case 'trust':
          return request<void>('PUT', `${base}/trust`, { action: change.action, level: change.level })
        case 'birthday':
          return request<void>('PUT', `${base}/birthday`, { month: change.month, day: change.day })
        case 'birthday-reset':
          return request<void>('DELETE', `${base}/birthday`)
      }
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: key(officeId) })
      await client.invalidateQueries({ queryKey: ['offices'] })
    },
  })
}
