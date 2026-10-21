import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { mergeActivity } from '../activity/activity'
import { keys } from '../api/hooks'
import type { DeskActivity } from '../api/types'
import { retryDelay } from './retry'

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
    hub.on('TaskChanged', () => void client.invalidateQueries({ queryKey: keys.tasks }))
    hub.on('TaskLogChanged', (taskId: string) => void client.invalidateQueries({ queryKey: keys.log(taskId) }))
    hub.on('DeskActivity', (activity: DeskActivity) =>
      client.setQueryData<DeskActivity[]>(keys.activity, (list) => mergeActivity(list, activity)),
    )
    hub.onreconnecting(() => setState('connecting'))
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
