/** Whether a paper's settings ask for the kanji of the day: "kanji": { "enabled": true }. */
export function asksForKanji(kind: string, settings: string): boolean {
  if (kind !== 'morning-paper') return false
  try {
    const parsed: unknown = JSON.parse(settings)
    const kanji = typeof parsed === 'object' && parsed !== null ? (parsed as { kanji?: unknown }).kanji : undefined
    return typeof kanji === 'object' && kanji !== null && (kanji as { enabled?: unknown }).enabled === true
  } catch {
    return false
  }
}

/** "School year 2", or the rest of the common use list. */
export function gradeLabel(grade: number): string {
  return grade <= 6 ? `school year ${grade}` : 'secondary school'
}
