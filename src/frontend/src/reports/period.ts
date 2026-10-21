export const LANGUAGES = [
  { name: 'Italian', label: 'Italiano', locale: 'it-IT' },
  { name: 'English', label: 'English', locale: 'en-GB' },
  { name: 'Spanish', label: 'Español', locale: 'es-ES' },
  { name: 'German', label: 'Deutsch', locale: 'de-DE' },
  { name: 'French', label: 'Français', locale: 'fr-FR' },
] as const

export type LanguageName = (typeof LANGUAGES)[number]['name']

/** The local calendar day of an <input type="date"> value, as an instant range [from, to). */
export function dayRange(date: string): { from: string; to: string } {
  const [y, m, d] = date.split('-').map(Number)
  const from = new Date(y, m - 1, d)
  const to = new Date(y, m - 1, d + 1)
  return { from: from.toISOString(), to: to.toISOString() }
}

/** "21 ottobre 2026": how the chosen language names that day. */
export function dayLabel(date: string, language: LanguageName): string {
  const [y, m, d] = date.split('-').map(Number)
  const locale = LANGUAGES.find((l) => l.name === language)?.locale ?? 'en-GB'
  return new Date(y, m - 1, d).toLocaleDateString(locale, { day: 'numeric', month: 'long', year: 'numeric' })
}

/** Today as an <input type="date"> value, in local time. */
export function today(): string {
  const now = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}
