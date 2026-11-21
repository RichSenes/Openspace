/** What a connected account lets routines do, in words. */
export function allows(scopes: string[]): string {
  const can = [
    scopes.some((s) => s.endsWith('/gmail.modify')) && 'mail',
    scopes.some((s) => s.endsWith('/calendar.events')) && 'calendar',
    scopes.some((s) => s.endsWith('/tasks')) && 'tasks',
  ].filter(Boolean)
  return can.length ? `Routines can use its ${can.join(', ').replace(/, ([^,]*)$/, ' and $1')}.` : 'Routines can use nothing of it: connect it again.'
}

export type GoogleStep = 'client' | 'account' | 'done'

/** Where the person is: first the OAuth client, then a first account. */
export function googleStep(status: { clientReady: boolean; accounts: unknown[] } | undefined): GoogleStep {
  if (!status?.clientReady) return 'client'
  return status.accounts.length === 0 ? 'account' : 'done'
}

const DOWNLOAD = 'Download it from Google Cloud: Credentials, your OAuth client (a desktop app), Download JSON.'

/** What is wrong with a chosen client file, said before it leaves the page; null when it looks like one Google gave. */
export function clientFileProblem(text: string): string | null {
  let root: unknown
  try {
    root = JSON.parse(text)
  } catch {
    return `This file is not valid JSON. ${DOWNLOAD}`
  }
  if (typeof root !== 'object' || root === null || Array.isArray(root)) return `This is not an OAuth client file. ${DOWNLOAD}`
  const file = root as Record<string, unknown>
  if (file.type === 'service_account') return `This is a service account key, not an OAuth client. ${DOWNLOAD}`
  const client = file.installed ?? file.web ?? file
  if (typeof client !== 'object' || client === null) return `This is not an OAuth client file. ${DOWNLOAD}`
  const fields = client as Record<string, unknown>
  if (typeof fields.client_id !== 'string' || fields.client_id.trim() === '') return `The file has no client_id. ${DOWNLOAD}`
  if (typeof fields.client_secret !== 'string' || fields.client_secret.trim() === '') return `The file has no client_secret. ${DOWNLOAD}`
  return null
}
