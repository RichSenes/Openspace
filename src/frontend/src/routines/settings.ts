const objectOf = (text: string): Record<string, unknown> | undefined => {
  try {
    const parsed: unknown = JSON.parse(text)
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : undefined
  } catch {
    return undefined
  }
}

/** A text setting of a routine, empty when it is missing or the settings are not valid JSON. */
export function readText(settings: string, key: string): string {
  const value = objectOf(settings)?.[key]
  return typeof value === 'string' ? value : ''
}

/** The settings with one text setting changed and everything else as it was; unchanged while they are not valid JSON. */
export function withText(settings: string, key: string, value: string): string {
  const parsed = settings.trim() ? objectOf(settings) : {}
  return parsed ? JSON.stringify({ ...parsed, [key]: value }, null, 2) : settings
}

/** Where a mail routine puts the dates it finds: "agenda": { "tasks": true, "calendar": true }; both when unsaid, neither when false. */
export function readAgenda(settings: string): { tasks: boolean; calendar: boolean } {
  const agenda = objectOf(settings)?.agenda
  if (agenda === false) return { tasks: false, calendar: false }
  const choice = typeof agenda === 'object' && agenda !== null ? (agenda as { tasks?: unknown; calendar?: unknown }) : {}
  return { tasks: choice.tasks !== false, calendar: choice.calendar !== false }
}

export function withAgenda(settings: string, agenda: { tasks: boolean; calendar: boolean }): string {
  const parsed = settings.trim() ? objectOf(settings) : {}
  return parsed ? JSON.stringify({ ...parsed, agenda }, null, 2) : settings
}
