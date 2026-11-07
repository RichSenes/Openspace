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
  helpers: 'Deny',
  skills: null,
}

export function formFrom(role: RoleView | undefined): RoleInput {
  if (!role) return EMPTY_ROLE
  const { id: _id, ...input } = role
  return input
}

/** Helpers are on or off: asking at every hand-off would stop the agent each time. */
export const HELPER_LABEL: Partial<Record<PermissionLevel, string>> = {
  Deny: 'Not allowed',
  Allow: 'Allowed',
}

/**
 * "Change files: asks you · Run commands: not allowed", leaving out what is simply allowed;
 * helpers are off unless the role turns them on, so only that is said.
 */
export function permissionSummary(role: Pick<RoleView, 'edit' | 'bash' | 'web'> & Partial<Pick<RoleView, 'helpers'>>): string {
  const limits = CAPABILITIES.filter((c) => role[c.key] !== 'Allow').map(
    (c) => `${c.label}: ${PERMISSION_LABEL[role[c.key]].toLowerCase()}`,
  )
  const helpers = role.helpers === 'Allow' ? ['Can call in interns'] : []
  return limits.length === 0 ? ['Works freely', ...helpers].join(' · ') : [...limits, ...helpers].join(' · ')
}

/** "Every installed skill", or how many the role names. */
export function skillsSummary(skills: string[] | null): string {
  if (skills === null) return 'Every installed skill'
  if (skills.length === 0) return 'No skills'
  return skills.length === 1 ? `Skill: ${skills[0]}` : `${skills.length} skills`
}

/** Adds or removes one skill from the role's list, keeping it sorted. */
export function toggleSkill(skills: string[], name: string): string[] {
  return skills.includes(name) ? skills.filter((s) => s !== name) : [...skills, name].sort()
}
