import { useSyncExternalStore } from 'react'

/** What each running routine said last, by routine id; nothing once it stopped. */
let lines: ReadonlyMap<string, string> = new Map()
const listeners = new Set<() => void>()

/** A routine said what it is doing; an empty line means its run ended. */
export function reportRoutineProgress(routineId: string, line: string) {
  const next = new Map(lines)
  if (line) next.set(routineId, line)
  else next.delete(routineId)
  lines = next
  listeners.forEach((l) => l())
}

export function useRoutineProgress(): ReadonlyMap<string, string> {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => lines,
  )
}
