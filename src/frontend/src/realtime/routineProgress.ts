import { useSyncExternalStore } from 'react'

/** What a running routine said last, and when it was heard, in milliseconds. */
export interface RoutineNews {
  line: string
  at: number
}

/** What each running routine said last, by routine id; nothing once it stopped. */
let news: ReadonlyMap<string, RoutineNews> = new Map()
const listeners = new Set<() => void>()

/** A routine said what it is doing; an empty line means its run ended. */
export function reportRoutineProgress(routineId: string, line: string, at: number = Date.now()) {
  const next = new Map(news)
  if (line) next.set(routineId, { line, at })
  else next.delete(routineId)
  news = next
  listeners.forEach((l) => l())
}

export function useRoutineProgress(): ReadonlyMap<string, RoutineNews> {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => news,
  )
}
