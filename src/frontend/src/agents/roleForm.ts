import type { PermissionLevel, RoleInput, RoleView } from '../api/types'

export const PERMISSION_LABEL: Record<PermissionLevel, string> = {
  Allow: 'Allowed',
  Ask: 'Asks you',
  Deny: 'Not allowed',
}

export const CAPABILITIES = [
  { key: 'edit', label: 'Change files' },
  { key: 'bash', label: 'Run commands' },
  { key: 'web', label: 'Use the web' },
] as const

export const EMPTY_ROLE: RoleInput = {
  name: '',
  description: '',
  instructions: '',
  model: null,
  edit: 'Allow',
  bash: 'Allow',
  web: 'Allow',
}

export function formFrom(role: RoleView | undefined): RoleInput {
  if (!role) return EMPTY_ROLE
  const { id: _id, ...input } = role
  return input
}

/** "Change files: asks you · Run commands: not allowed", leaving out what is simply allowed. */
export function permissionSummary(role: Pick<RoleView, 'edit' | 'bash' | 'web'>): string {
  const limits = CAPABILITIES.filter((c) => role[c.key] !== 'Allow').map(
    (c) => `${c.label}: ${PERMISSION_LABEL[role[c.key]].toLowerCase()}`,
  )
  return limits.length === 0 ? 'Works freely' : limits.join(' · ')
}
