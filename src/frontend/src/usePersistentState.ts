import { useEffect, useState } from 'react'

/** useState that remembers its value in this browser. Storage can fail (private mode): then it just forgets. */
export function usePersistentState<T extends string>(key: string, fallback: T): [T, (value: T) => void] {
  const [value, setValue] = useState<T>(() => {
    try {
      return (localStorage.getItem(key) as T | null) ?? fallback
    } catch {
      return fallback
    }
  })

  useEffect(() => {
    try {
      localStorage.setItem(key, value)
    } catch {
      // Not persisted; the page still works.
    }
  }, [key, value])

  return [value, setValue]
}
