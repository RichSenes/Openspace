import { useSyncExternalStore } from 'react'
import type { QueryClient } from '@tanstack/react-query'
import { keys } from '../api/hooks'
import type { ShioriSessionView, ShioriState, TranscriptEntry, WhiteboardItem } from '../api/types'
import { applyEvent, liveReducer, NO_LIVE, sessionOf, type Live, type LiveEvent } from './live'

/** The live part of every session the hub spoke about, by id. */
let lives: ReadonlyMap<string, Live> = new Map()
const listeners = new Set<() => void>()

/** Feeds one hub event to the live store and to the session held in the query cache. */
export function reportShiori(client: QueryClient, event: LiveEvent) {
  const id = sessionOf(event)
  const before = lives.get(id) ?? NO_LIVE
  const after = liveReducer(before, event)
  if (after !== before) {
    const next = new Map(lives)
    next.set(id, after)
    lives = next
    listeners.forEach((l) => l())
  }
  client.setQueryData<ShioriSessionView>(keys.shioriSession(id), (view) => (view ? applyEvent(view, event) : view))
  if (event.type !== 'text') void client.invalidateQueries({ queryKey: keys.shioriSessions, exact: true })
}

/** The hub's four Shiori messages, wired to the store. */
export function listenToShiori(hub: { on: (name: string, handler: (...args: never[]) => void) => void }, client: QueryClient) {
  hub.on('ShioriText', (sessionId: string, chunk: string) => reportShiori(client, { type: 'text', sessionId, chunk }))
  hub.on('ShioriMessage', (sessionId: string, entry: TranscriptEntry) => reportShiori(client, { type: 'message', sessionId, entry }))
  hub.on('ShioriBoard', (sessionId: string, board: WhiteboardItem[]) => reportShiori(client, { type: 'board', sessionId, board }))
  hub.on('ShioriState', (state: ShioriState) => reportShiori(client, { type: 'state', state }))
  // In development a page can be shown any moment of an answer without an engine.
  if (import.meta.env.DEV) (window as unknown as { __shioriEvent?: (e: LiveEvent) => void }).__shioriEvent = (e) => reportShiori(client, e)
}

export function useLive(sessionId: string | undefined): Live {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => (sessionId ? (lives.get(sessionId) ?? NO_LIVE) : NO_LIVE),
  )
}
