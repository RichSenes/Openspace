import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { keys } from '../api/hooks'

export type LinkState = 'connecting' | 'live' | 'offline'

/** Keeps the page in sync with the server: every change pushed by the hub refetches what it touches. */
export function useBuildingHub(): LinkState {
  const client = useQueryClient()
  const [state, setState] = useState<LinkState>('connecting')

  useEffect(() => {
    const hub = new HubConnectionBuilder()
      .withUrl('/hubs/building')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    hub.on('OfficeChanged', () => void client.invalidateQueries({ queryKey: keys.offices }))
    hub.on('TaskChanged', () => void client.invalidateQueries({ queryKey: keys.tasks }))
    hub.onreconnecting(() => setState('connecting'))
    hub.onreconnected(() => {
      setState('live')
      void client.invalidateQueries()
    })
    hub.onclose(() => setState('offline'))

    let stopped = false
    hub
      .start()
      .then(() => !stopped && setState('live'))
      .catch(() => !stopped && setState('offline'))

    return () => {
      stopped = true
      if (hub.state !== HubConnectionState.Disconnected) void hub.stop()
    }
  }, [client])

  return state
}
