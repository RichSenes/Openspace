import { useState } from 'react'
import { ShieldCheck, ShieldPlus, Users, Wrench } from 'lucide-react'
import {
  useAddSecurityDesk,
  useHandOff,
  useProposeFix,
  useRoles,
  useSecurityAudits,
  useSecurityFindings,
  useSecurityReport,
  useStartSecurityAudit,
} from '../api/hooks'
import type { OfficeView, SecurityAuditProfile, SecurityAuditView, SecurityFindingView } from '../api/types'
import { Markdown } from '../reports/Markdown'
import { LANGUAGES, type LanguageName } from '../reports/period'
import { usePersistentState } from '../usePersistentState'
import { StatusPill } from '../ui/StatusPill'
import { useToast } from '../ui/toastContext'
import { auditorDesks, groupFindings, handOffMessage, openFindings, PROFILES, runSummary, severitySummary } from './securityRules'
import '../reports/reports.css'
import './security.css'

const when = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/**
 * An office's security audits: the auditor desk reads the code for real holes with the
 * security-audit skill, proves each one, and never changes the code. Fixes become tasks.
 */
export function SecurityPanel({ office }: { office: OfficeView }) {
  const roles = useRoles()
  const audits = useSecurityAudits(office.id)
  const addDesk = useAddSecurityDesk(office.id)
  const start = useStartSecurityAudit(office.id)
  const toast = useToast()
  const [profile, setProfile] = useState<SecurityAuditProfile>('Quick')
  const [language, setLanguage] = usePersistentState<LanguageName>('openspace.report-language', 'Italian')
  const [handOff, setHandOff] = usePersistentState<'on' | 'off'>('openspace.security-hand-off', 'on')
  const [openRun, setOpenRun] = useState<number>()
  const auditors = auditorDesks(office, roles.data)
  const open = audits.data?.find((a) => a.run.number === openRun) ?? audits.data?.[0]
  const busy = audits.data?.some((a) => a.status === 'Queued' || a.status === 'Running') ?? false

  return (
    <section className="reports security">
      <header className="reports-head">
        <h1>Security of {office.name}</h1>
        <p className="hint">
          The auditor reads the code for real holes with Cloudflare&apos;s security-audit skill: its interns hunt, a fresh one tries to
          disprove every finding, and only what survives is confirmed. It never changes the code: fixes become tasks you approve.
        </p>
      </header>

      {!office.repositoryPath ? (
        <div className="empty">
          <h2>Nothing to audit yet</h2>
          <p>This office has no repository. Give it one in Rooms, then add a security desk here.</p>
        </div>
      ) : auditors.length === 0 ? (
        <div className="empty">
          <h2>No security desk yet</h2>
          <p>
            Adding one opens a Security room with an Auditor desk on Claude Code (sonnet), and a Security auditor role that loads the
            security-audit skill and may call in interns. You can change its model or engine afterwards in Rooms.
          </p>
          <button
            type="button"
            className="btn btn-primary"
            disabled={addDesk.isPending || !office.repositoryPath}
            onClick={() => addDesk.mutate(null, { onSuccess: () => toast('Security desk added') })}
          >
            <ShieldPlus size={16} /> Add a security desk
          </button>
          {addDesk.error && <p className="error">{addDesk.error.message}</p>}
        </div>
      ) : (
        <form
          className="report-forms security-start"
          onSubmit={(e) => {
            e.preventDefault()
            start.mutate({ profile, language, handOff: handOff === 'on' }, {
              onSuccess: () => {
                setOpenRun(undefined)
                toast('Audit started: the auditor takes it from the queue')
              },
            })
          }}
        >
          <label className="field">
            <span>Depth</span>
            <select className="input" value={profile} onChange={(e) => setProfile(e.target.value as SecurityAuditProfile)}>
              {PROFILES.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.label}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Written in</span>
            <select className="input" value={language} onChange={(e) => setLanguage(e.target.value as LanguageName)}>
              {LANGUAGES.map((l) => (
                <option key={l.name} value={l.name}>
                  {l.label}
                </option>
              ))}
            </select>
          </label>
          <p className="hint security-profile-hint">{PROFILES.find((p) => p.id === profile)?.hint}</p>
          <button type="submit" className="btn btn-primary" disabled={start.isPending || busy}>
            <ShieldCheck size={16} /> Start audit
          </button>
          <label className="security-hand-off">
            <input type="checkbox" checked={handOff === 'on'} onChange={(e) => setHandOff(e.target.checked ? 'on' : 'off')} />
            <span>When it finishes, hand the findings to the supervisor, who proposes the fixes at the right desks</span>
          </label>
          {busy && <p className="hint security-busy">An audit is already queued or running: one at a time keeps your plan in check.</p>}
          {start.error && <p className="error">{start.error.message}</p>}
        </form>
      )}

      {audits.error ? (
        <p className="error">{audits.error.message}</p>
      ) : audits.data?.length === 0 ? (
        <p className="hint">No audits yet. Each run keeps its records outside the repository, so the next one builds on them.</p>
      ) : (
        <div className="reports-body">
          <ul className="report-list" aria-label="Audits">
            {audits.data?.map((a) => (
              <li key={a.run.number}>
                <button
                  type="button"
                  className="report-item"
                  aria-current={a.run.number === open?.run.number ? 'true' : undefined}
                  onClick={() => setOpenRun(a.run.number)}
                >
                  <span className="report-title">
                    Run {a.run.number}
                    {a.run.profile && ` · ${a.run.profile}`}
                  </span>
                  <span className="hint">{when(a.run.startedAt)}</span>
                  <span className="hint">{runSummary(a)}</span>
                  {a.status && !a.run.hasReport && <StatusPill status={a.status} />}
                </button>
              </li>
            ))}
          </ul>
          {open && <AuditRun key={open.run.number} office={office} audit={open} language={language} />}
        </div>
      )}
    </section>
  )
}

function AuditRun({ office, audit, language }: { office: OfficeView; audit: SecurityAuditView; language: string }) {
  const { run } = audit
  const report = useSecurityReport(office.id, run.number, run.hasReport)
  const findings = useSecurityFindings(office.id, run.number)
  const groups = groupFindings(findings.data ?? [])
  const severities = severitySummary(run.severities)
  const open = openFindings(findings.data ?? [])
  const handAll = useHandOff(office.id)
  const toast = useToast()

  return (
    <article className="report">
      <header className="report-actions">
        <span className="hint">
          Run {run.number} of {when(run.startedAt)}
          {audit.deskName && ` at ${audit.deskName}`}
          {run.sourceRef && ` · code at ${run.sourceRef.slice(0, 10)}`}
        </span>
        {severities && <span className="security-severities">{severities}</span>}
      </header>

      {run.hasReport && open.length > 0 && (
        <div className="security-hand-all">
          <button
            type="button"
            className="btn btn-primary"
            disabled={handAll.isPending}
            onClick={() => handAll.mutate({ run: run.number, language }, { onSuccess: (r) => toast(handOffMessage(r)) })}
          >
            <Users size={16} /> {handAll.isPending ? 'The supervisor is planning…' : `Hand ${open.length === 1 ? 'the open finding' : `all ${open.length} open findings`} to the supervisor`}
          </button>
          <span className="hint">It proposes one task per finding at the desk that works on that code; you approve them on the Board.</span>
          {handAll.error && <p className="error">{handAll.error.message}</p>}
        </div>
      )}
      {run.hasReport && open.length === 0 && (findings.data?.some((f) => f.handed) ?? false) && (
        <p className="hint">Every open finding is with the supervisor: its tasks wait for you on the Board.</p>
      )}

      {!run.hasReport && (
        <p className="hint">
          {audit.status === 'Running'
            ? 'The auditor and its interns are at work: you can watch them in the building. The report appears here when they finish.'
            : audit.status === 'Queued'
              ? 'Waiting for the auditor desk to be free.'
              : 'This run has no report. Its task notes on the Board say how it ended.'}
        </p>
      )}

      {groups.confirmed.length > 0 && (
        <section className="findings" aria-label="Confirmed findings">
          <h2>Confirmed</h2>
          {groups.confirmed.map((f) => (
            <Finding key={f.fingerprint} office={office} run={run.number} finding={f} language={language} />
          ))}
        </section>
      )}

      {groups.toVerify.length > 0 && (
        <section className="findings" aria-label="Findings to verify">
          <h2>To verify</h2>
          <p className="hint">
            Each needs one fact the auditor could not check without running the code. A fix for one of these starts with a test that
            proves the hole, and stops there if it does not.
          </p>
          {groups.toVerify.map((f) => (
            <Finding key={f.fingerprint} office={office} run={run.number} finding={f} language={language} />
          ))}
        </section>
      )}

      {groups.rejected.length > 0 && (
        <details className="findings">
          <summary>
            {groups.rejected.length} rejected: candidates a verifier disproved
          </summary>
          {groups.rejected.map((f) => (
            <Finding key={f.fingerprint} office={office} run={run.number} finding={f} language={language} />
          ))}
        </details>
      )}

      {report.data && (
        <details className="findings security-report">
          <summary>The full report, as the auditor wrote it</summary>
          <Markdown text={report.data} />
        </details>
      )}
    </article>
  )
}

function Finding({ office, run, finding, language }: { office: OfficeView; run: number; finding: SecurityFindingView; language: string }) {
  const fix = useProposeFix(office.id)
  const toast = useToast()

  return (
    <div className="finding">
      <div className="finding-head">
        <span className="finding-title">{finding.title}</span>
        {finding.severity && <span className={`finding-severity severity-${finding.severity}`}>{finding.severity}</span>}
      </div>
      {finding.location && <code className="finding-where">{finding.location}</code>}
      {finding.description && <p>{finding.description}</p>}
      {finding.remediation && <p className="hint">How to fix it: {finding.remediation}</p>}
      {(finding.verdict === 'confirmed' || finding.verdict === 'needs_validation') &&
        (finding.handed ? (
          <p className="hint">With the supervisor: its task waits for you on the Board.</p>
        ) : (
          <div className="row">
            <button
              type="button"
              className="btn"
              disabled={fix.isPending}
              onClick={() => fix.mutate({ run, fingerprint: finding.fingerprint, language }, { onSuccess: (r) => toast(handOffMessage(r)) })}
            >
              <Wrench size={16} /> {fix.isPending ? 'The supervisor is planning…' : finding.verdict === 'confirmed' ? 'Hand this fix to the supervisor' : 'Hand to the supervisor to verify and fix'}
            </button>
            {fix.error && <p className="error">{fix.error.message}</p>}
          </div>
        ))}
    </div>
  )
}
