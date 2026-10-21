/** An item on screen, possibly on its way out so its exit can be animated. */
export interface Shown<T> {
  key: string
  item: T
  leaving: boolean
}

/**
 * Merges what should be on screen now with what is on screen: new items enter,
 * items that went away stay a moment marked as leaving, the rest are refreshed.
 */
export function mergeForExit<T>(shown: Shown<T>[], current: T[], keyOf: (item: T) => string): Shown<T>[] {
  const now = new Map(current.map((item) => [keyOf(item), item]))
  const kept = shown.map((s) => (now.has(s.key) ? { key: s.key, item: now.get(s.key)!, leaving: false } : { ...s, leaving: true }))
  const known = new Set(shown.map((s) => s.key))
  const added = current.filter((item) => !known.has(keyOf(item))).map((item) => ({ key: keyOf(item), item, leaving: false }))
  return [...kept, ...added]
}
