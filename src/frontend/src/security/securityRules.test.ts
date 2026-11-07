import { describe, expect, it } from 'vitest'
import type { OfficeView, RoleView, SecurityAuditView, SecurityFindingView } from '../api/types'
import { auditorDesks, groupFindings, openFindings, runSummary, severitySummary } from './securityRules'

const run = (over: Partial<SecurityAuditView['run']> = {}): SecurityAuditView['run'] => ({
  number: 1,
  startedAt: '2015-06-24T09:00:00Z',
  taskId: 't1',
  profile: 'quick',
  sourceRef: null,
  confirmed: 0,
  needsValidation: 0,
  rejected: 0,
  severities: {},
  hasReport: false,
  handOff: false,
  handedOff: false,
  ...over,
})

describe('runSummary', () => {
  it('says where an unfinished run is', () => {
    expect(runSummary({ run: run(), status: 'Queued', deskName: 'Auditor' })).toBe('Waiting for its desk')
    expect(runSummary({ run: run(), status: 'Running', deskName: 'Auditor' })).toBe('The auditor is at work')
    expect(runSummary({ run: run(), status: 'Failed', deskName: 'Auditor' })).toBe('Stopped before its report')
    expect(runSummary({ run: run(), status: null, deskName: null })).toBe('Its task is gone')
  })

  it('counts the verdicts of a finished run', () => {
    expect(runSummary({ run: run({ hasReport: true, confirmed: 2, needsValidation: 1, rejected: 3 }), status: 'Review', deskName: 'Auditor' })).toBe(
      '2 confirmed · 1 to verify · 3 rejected',
    )
  })
})

describe('severitySummary', () => {
  it('lists the worst first and leaves out what is not there', () => {
    expect(severitySummary({ medium: 2, critical: 1, low: 0 })).toBe('1 critical · 2 medium')
    expect(severitySummary({})).toBe('')
  })
})

describe('groupFindings', () => {
  const finding = (fingerprint: string, verdict: string, severity: SecurityFindingView['severity'] = null): SecurityFindingView => ({
    fingerprint,
    verdict,
    title: fingerprint,
    description: '',
    severity,
    location: null,
    remediation: null,
    handed: false,
  })

  it('puts confirmed findings worst first, apart from the rest', () => {
    const groups = groupFindings([finding('a', 'confirmed', 'low'), finding('b', 'rejected'), finding('c', 'confirmed', 'critical'), finding('d', 'needs_validation')])

    expect(groups.confirmed.map((f) => f.fingerprint)).toEqual(['c', 'a'])
    expect(groups.toVerify.map((f) => f.fingerprint)).toEqual(['d'])
    expect(groups.rejected.map((f) => f.fingerprint)).toEqual(['b'])
  })
})

describe('auditorDesks', () => {
  it('finds the desks whose role names the security-audit skill', () => {
    const roles = [
      { id: 'r1', skills: ['security-audit'] },
      { id: 'r2', skills: null },
    ] as RoleView[]
    const office = {
      rooms: [
        { desks: [{ id: 'd1', roleId: 'r1' }, { id: 'd2', roleId: 'r2' }, { id: 'd3', roleId: null }] },
      ],
    } as unknown as OfficeView

    expect(auditorDesks(office, roles).map((d) => d.id)).toEqual(['d1'])
    expect(auditorDesks(office, undefined)).toEqual([])
  })
})

describe('hand-off', () => {
  it('keeps only the findings still to hand over', () => {
    const base = { title: '', description: '', severity: null, location: null, remediation: null }
    const findings = [
      { ...base, fingerprint: 'a', verdict: 'confirmed', handed: false },
      { ...base, fingerprint: 'b', verdict: 'needs_validation', handed: true },
      { ...base, fingerprint: 'c', verdict: 'rejected', handed: false },
      { ...base, fingerprint: 'd', verdict: 'needs_validation', handed: false },
    ]
    expect(openFindings(findings).map((f) => f.fingerprint)).toEqual(['a', 'd'])
  })
})
