/** What a connected account lets routines do, in words. */
export function allows(scopes: string[]): string {
  const can = [
    scopes.some((s) => s.endsWith('/gmail.modify')) && 'mail',
    scopes.some((s) => s.endsWith('/calendar.events')) && 'calendar',
    scopes.some((s) => s.endsWith('/tasks')) && 'tasks',
  ].filter(Boolean)
  return can.length ? `Routines can use its ${can.join(', ').replace(/, ([^,]*)$/, ' and $1')}.` : 'Routines can use nothing of it: connect it again.'
}
