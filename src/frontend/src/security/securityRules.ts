import type { FindingSeverity, OfficeView, RoleView, SecurityAuditProfile, SecurityAuditView, SecurityFindingView } from '../api/types'

/** The skill a desk's role names when that desk audits. */
export const AUDIT_SKILL = 'security-audit'

export const PROFILES: { id: SecurityAuditProfile; label: string; hint: string }[] = [
  { id: 'Quick', label: 'Quick', hint: 'A first look, one round of hunters. The lightest on your Claude plan.' },
  { id: 'Standard', label: 'Standard', hint: 'The full method: hunters, critics and independent verifiers.' },
  { id: 'Deep', label: 'Deep', hint: 'Every subsystem on its own, until a clean pass. Uses much more of your plan.' },
]

const SEVERITY_ORDER: FindingSeverity[] = ['critical', 'high', 'medium', 'low', 'informational']

/** The office's desks that audit: their role names the security-audit skill. */
export function auditorDesks(office: OfficeView, roles: RoleView[] | undefined) {
  const auditing = new Set((roles ?? []).filter((r) => r.skills?.includes(AUDIT_SKILL)).map((r) => r.id))
  return office.rooms.flatMap((room) => room.desks.filter((d) => d.roleId !== null && auditing.has(d.roleId)))
}

/** "2 confirmed · 1 to verify · 1 rejected", or a word about where the run is. */
export function runSummary(audit: SecurityAuditView): string {
  const { run, status } = audit
  if (!run.hasReport) {
    if (status === 'Queued') return 'Waiting for its desk'
    if (status === 'Running') return 'The auditor is at work'
    if (status === 'Failed') return 'Stopped before its report'
    if (status === null) return 'Its task is gone'
    return 'No report written'
  }

  return [`${run.confirmed} confirmed`, `${run.needsValidation} to verify`, `${run.rejected} rejected`].join(' · ')
}

/** "1 high · 2 medium", worst first; empty when nothing is confirmed. */
export function severitySummary(severities: SecurityAuditView['run']['severities']): string {
  return SEVERITY_ORDER.filter((s) => (severities[s] ?? 0) > 0)
    .map((s) => `${severities[s]} ${s}`)
    .join(' · ')
}

/** Findings grouped for reading: confirmed worst first, then those to verify, then the rejected. */
export function groupFindings(findings: SecurityFindingView[]) {
  const rank = (f: SecurityFindingView) => (f.severity ? SEVERITY_ORDER.indexOf(f.severity) : SEVERITY_ORDER.length)
  return {
    confirmed: findings.filter((f) => f.verdict === 'confirmed').sort((a, b) => rank(a) - rank(b)),
    toVerify: findings.filter((f) => f.verdict === 'needs_validation'),
    rejected: findings.filter((f) => f.verdict === 'rejected'),
  }
}
