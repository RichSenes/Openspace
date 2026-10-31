import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { mergeActivity } from '../activity/activity'
import { mergeContext } from '../activity/energy'
import { reportRoutineProgress } from './routineProgress'
import { keys } from '../api/hooks'
import type { DeskActivity, DeskContext } from '../api/types'
import { retryDelay } from './retry'
import { cueOfActivity, emitCue, type SoundEvent } from '../sounds/rules'

export type LinkState = 'connecting' | 'live' | 'offline'

/** Keeps the page in sync with the server: every change pushed by the hub refetches what it touches. */
export function useBuildingHub(): LinkState {
  const client = useQueryClient()
  const [state, setState] = useState<LinkState>('connecting')

  useEffect(() => {
    const hub = new HubConnectionBuilder()
      .withUrl('/hubs/building')
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => retryDelay(context.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build()

    hub.on('OfficeChanged', () => void client.invalidateQueries({ queryKey: keys.offices }))
    hub.on('TaskChanged', () => {
      void client.invalidateQueries({ queryKey: keys.feed })
      void client.invalidateQueries({ queryKey: keys.tasks })
      void client.invalidateQueries({ queryKey: keys.deskUsage })
    })
    hub.on('ReportsChanged', () => void client.invalidateQueries({ queryKey: keys.reports }))
    hub.on('RoutinesChanged', () => void client.invalidateQueries({ queryKey: keys.routines }))
    hub.on('RoutineProgress', (progress: { routineId: string; line: string }) => reportRoutineProgress(progress.routineId, progress.line))
    hub.on('EpicsChanged', () => void client.invalidateQueries({ queryKey: keys.epics }))
    hub.on('RolesChanged', () => void client.invalidateQueries({ queryKey: keys.roles }))
    hub.on('TaskLogChanged', (taskId: string) => {
      void client.invalidateQueries({ queryKey: keys.log(taskId) })
      void client.invalidateQueries({ queryKey: keys.feed })
    })
    hub.on('DeskActivity', (activity: DeskActivity) => {
      client.setQueryData<DeskActivity[]>(keys.activity, (list) => mergeActivity(list, activity))
      const cue = cueOfActivity(activity)
      if (cue) emitCue(cue)
    })
    hub.on('DeskContext', (context: DeskContext) => client.setQueryData<DeskContext[]>(keys.context, (list) => mergeContext(list, context)))
    hub.on('TaskCue', (c: { taskId: string | null; officeId: string | null; deskId: string | null; cue: SoundEvent }) =>
      emitCue({ event: c.cue, officeId: c.officeId, deskId: c.deskId, key: `${c.taskId ?? c.officeId}:${c.cue}:${Date.now()}` }),
    )
    hub.onreconnecting(() => {
      setState('connecting')
      emitCue({ event: 'offline', key: `offline:${Date.now()}` })
    })
    hub.onreconnected(() => {
      setState('live')
      void client.invalidateQueries()
    })
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | undefined

    // The first connection (or one lost for good) is retried by hand: automatic
    // reconnect only covers connections that were up at least once.
    const connect = (attempt: number) => {
      hub
        .start()
        .then(() => {
          if (stopped) return
          setState('live')
          if (attempt > 0) void client.invalidateQueries()
        })
        .catch(() => {
          if (stopped) return
          setState('offline')
          timer = setTimeout(() => connect(attempt + 1), retryDelay(attempt))
        })
    }

    hub.onclose(() => {
      if (stopped) return
      setState('offline')
      timer = setTimeout(() => connect(1), retryDelay(0))
    })
    connect(0)

    return () => {
      stopped = true
      clearTimeout(timer)
      if (hub.state !== HubConnectionState.Disconnected) void hub.stop()
    }
  }, [client])

  return state
}
