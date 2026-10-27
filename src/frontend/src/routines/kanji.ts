/** The kanji of the day in a paper's settings: "kanji": { "enabled": true, "fromGrade": 1 }. */
export interface KanjiChoice {
  enabled: boolean
  fromGrade: number
}

const objectOf = (text: string): Record<string, unknown> | undefined => {
  try {
    const parsed: unknown = JSON.parse(text)
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : undefined
  } catch {
    return undefined
  }
}

/** What the settings say about the kanji of the day; off, from the first year, when they say nothing. */
export function readKanji(settings: string): KanjiChoice {
  const kanji = objectOf(settings)?.kanji as { enabled?: unknown; fromGrade?: unknown } | undefined
  const grade = typeof kanji?.fromGrade === 'number' && kanji.fromGrade >= 1 && kanji.fromGrade <= 8 ? kanji.fromGrade : 1
  return { enabled: kanji?.enabled === true, fromGrade: grade }
}

/** The settings with the kanji of the day changed and everything else as it was; unchanged while they are not valid JSON. */
export function withKanji(settings: string, choice: KanjiChoice): string {
  const parsed = settings.trim() ? objectOf(settings) : {}
  if (!parsed) return settings
  return JSON.stringify({ ...parsed, kanji: { ...(parsed.kanji as object), enabled: choice.enabled, fromGrade: choice.fromGrade } }, null, 2)
}

/** Whether a paper's settings ask for the kanji of the day. */
export function asksForKanji(kind: string, settings: string): boolean {
  return kind === 'morning-paper' && readKanji(settings).enabled
}

/** Where the kanji is taught in Japan: years 1 to 6 of primary school, then secondary school. */
export function gradeLabel(grade: number): string {
  return grade <= 6 ? `primary school, year ${grade}` : 'secondary school'
}

/** The school years a course can start from, as the settings count them. */
export const GRADES = [1, 2, 3, 4, 5, 6, 8]
