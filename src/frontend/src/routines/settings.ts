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
