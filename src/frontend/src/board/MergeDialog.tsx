import { useEffect, useRef, useState } from 'react'
import { Check, CircleSlash, GitMerge, ListChecks, TriangleAlert, X } from 'lucide-react'
import { useBranches, useCatchUp, useMergeAll, useRunChecks } from '../api/hooks'
import type { CheckOutcome, MergeOutcome, OfficeView, WorkTaskView } from '../api/types'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import type { DeskWithRoom } from './deskLookup'
import { defaultMergeTarget, mergeBlocker } from './mergeTarget'
import { awaitingMerge, checksDone } from './taskRules'

interface Props {
  office: OfficeView
  tasks: readonly WorkTaskView[]
  desks: DeskWithRoom[]
  open: boolean
  onClose: () => void
}

const when = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/**
 * Brings accepted work into one branch in a click, in the order it was accepted. What
 * conflicts goes back to its agent, which resolves it in its own working copy.
 */
export function MergeDialog({ office, tasks, desks, open, onClose }: Props) {
  const waiting = awaitingMerge(tasks)
  const branches = useBranches(office.id, open)
  const [chosenTarget, setTarget] = useState('')
  const target = chosenTarget || defaultMergeTarget(branches.data)
  const [skipped, setSkipped] = useState<ReadonlySet<string>>(new Set())
  const [outcomes, setOutcomes] = useState<MergeOutcome[]>([])
  const [caughtUp, setCaughtUp] = useState<ReadonlySet<string>>(new Set())
  const mergeAll = useMergeAll(office.id)
  const runChecks = useRunChecks(office.id)
  const [checks, setChecks] = useState<{ branch: string; outcomes: CheckOutcome[] }>()
  const hasChecks = office.rooms.some((r) => r.check)
  const catchUp = useCatchUp()
  const toast = useToast()
  const chosen = waiting.filter((t) => !skipped.has(t.id))
  const blocker = mergeBlocker(branches.data, target)
  const results = useRef<HTMLElement>(null)
  // The outcome shows below the button that asked for it, in view.
  useEffect(() => results.current?.scrollIntoView({ block: 'nearest' }), [outcomes])
  const deskOf = (taskId: string) => desks.find((d) => d.id === tasks.find((t) => t.id === taskId)?.deskId)?.name ?? 'its agent'

  const check = (branch: string, taskIds: string[] | null) => {
    setChecks(undefined)
    runChecks.mutate(
      { branch, taskIds },
      {
        onSuccess: (outcomes) => {
          setChecks({ branch, outcomes })
          const failed = outcomes.filter((o) => !o.passed).length
          if (outcomes.length > 0) toast(failed === 0 ? `Checks passed on ${branch}` : `${failed} of ${outcomes.length} checks failed on ${branch}`)
        },
      },
    )
  }

  const close = () => {
    setChecks(undefined)
    runChecks.reset()
    setOutcomes([])
    setCaughtUp(new Set())
    mergeAll.reset()
    catchUp.reset()
    onClose()
  }

  const toggle = (id: string) =>
    setSkipped((s) => {
      const next = new Set(s)
      if (!next.delete(id)) next.add(id)
      return next
    })

  const merge = () =>
    mergeAll.mutate(
      { target, taskIds: chosen.map((t) => t.id) },
      {
        onSuccess: (result) => {
          setOutcomes(result)
          const merged = result.filter((o) => o.result === 'Merged')
          toast(`${merged.length} of ${result.length} merged into ${target}`)
          // A clean merge can still break the code: check what was just merged.
          if (merged.length > 0 && hasChecks) check(target, merged.map((o) => o.taskId))
        },
      },
    )

  const sendToCatchUp = (outcome: MergeOutcome) =>
    catchUp.mutate(
      { taskId: outcome.taskId, target },
      {
        onSuccess: () => {
          setCaughtUp((s) => new Set(s).add(outcome.taskId))
          toast(`Back to ${deskOf(outcome.taskId)}: it resolves the conflicts`)
        },
      },
    )

  return (
    <Dialog title="Merge desk" open={open} onClose={close}>
      <div className="merge-desk">
        {waiting.length === 0 ? (
          <p className="hint">Nothing waiting to be merged. Accepted work shows up here until it is in one of your branches.</p>
        ) : (
          <>
            <label className="field">
              <span>Bring the work into</span>
              <select className="input mono" value={target} onChange={(e) => setTarget(e.target.value)}>
                {(branches.data?.branches ?? []).map((b) => (
                  <option key={b} value={b}>
                    {b}
                    {b === branches.data?.current ? ' (checked out)' : ''}
                  </option>
                ))}
              </select>
            </label>

            <fieldset className="merge-pick">
              <legend className="pixel notes-title">Accepted, not merged yet · first accepted first</legend>
              <ol className="merge-list">
                {waiting.map((t) => {
                  const checks = checksDone(t)
                  return (
                    <li key={t.id}>
                      <label className="check-row merge-row">
                        <input type="checkbox" checked={!skipped.has(t.id)} onChange={() => toggle(t.id)} />
                        <span className="merge-text">
                          <span>{t.title}</span>
                          <span className="hint">
                            {deskOf(t.id)} · accepted {when(t.statusChangedAt)}
                            {checks.total > checks.done && ` · ${checks.total - checks.done} of ${checks.total} checks not tried by hand`}
                          </span>
                        </span>
                      </label>
                    </li>
                  )
                })}
              </ol>
            </fieldset>

            {blocker && (
              <p className="merge-blocker" role="alert">
                <TriangleAlert size={14} aria-hidden /> {blocker}
              </p>
            )}
            <div className="row">
              <button type="button" className="btn btn-primary" disabled={!target || chosen.length === 0 || Boolean(blocker) || mergeAll.isPending} onClick={merge}>
                <GitMerge size={16} aria-hidden /> Merge {chosen.length} into {target || '…'}
              </button>
            </div>
            <p className="hint">
              Your checkout never moves: other branches are merged in a temporary copy. Work that conflicts is skipped and its agent can
              resolve it; with uncommitted changes on the branch nothing is touched.
            </p>
          </>
        )}
        {outcomes.length > 0 && (
          <section ref={results} className="merge-results" aria-label="What happened">
            <h3 className="pixel notes-title">
              Last round into {target} · {outcomes.filter((o) => o.result === 'Merged').length} of {outcomes.length} merged
            </h3>
            <ol className="merge-list">
              {outcomes.map((o) => (
                <li key={o.taskId} className={`merge-outcome merge-${o.result.toLowerCase()}`}>
                  <span className="merge-mark" aria-hidden="true">
                    {o.result === 'Merged' ? <Check size={14} /> : o.result === 'Conflict' ? <TriangleAlert size={14} /> : o.result === 'Failed' ? <X size={14} /> : <CircleSlash size={14} />}
                  </span>
                  <div className="merge-text">
                    <span>
                      <strong>{RESULT_LABEL[o.result]}</strong> · {o.title}
                    </span>
                    {o.message && <span className="hint">{o.message}</span>}
                    {o.result === 'Conflict' &&
                      (caughtUp.has(o.taskId) ? (
                        <span className="hint">Sent back to {deskOf(o.taskId)}: merge again once you accept it.</span>
                      ) : (
                        <button type="button" className="btn" disabled={catchUp.isPending} onClick={() => sendToCatchUp(o)}>
                          Let {deskOf(o.taskId)} resolve it
                        </button>
                      ))}
                  </div>
                </li>
              ))}
            </ol>
          </section>
        )}

        {(runChecks.isPending || checks) && (
          <section className="merge-results" aria-label="Checks">
            <h3 className="pixel notes-title">
              {runChecks.isPending ? `Checking ${target}… this can take minutes` : `Checks on ${checks?.branch}`}
            </h3>
            {checks?.outcomes.length === 0 && <p className="hint">None of these rooms has a check: add one in Rooms.</p>}
            <ol className="merge-list">
              {checks?.outcomes.map((c) => (
                <li key={c.roomId} className={`merge-outcome ${c.passed ? 'merge-merged' : 'merge-failed'}`}>
                  <span className="merge-mark" aria-hidden="true">
                    {c.passed ? <Check size={14} /> : <X size={14} />}
                  </span>
                  <div className="merge-text">
                    <span>
                      <strong>{c.passed ? 'Passed' : c.exitCode === null ? 'Stopped, too long' : `Failed, exit ${c.exitCode}`}</strong> · {c.room} ·{' '}
                      {c.seconds}s
                    </span>
                    <span className="hint mono">{c.command}</span>
                    <details open={!c.passed}>
                      <summary className="hint">Output</summary>
                      <pre className="check-output mono">{c.output || '(nothing printed)'}</pre>
                    </details>
                  </div>
                </li>
              ))}
            </ol>
          </section>
        )}
        {hasChecks && target && (
          <div className="row">
            <button type="button" className="btn" disabled={runChecks.isPending} onClick={() => check(target, null)}>
              <ListChecks size={16} aria-hidden /> Run all checks on {target}
            </button>
          </div>
        )}
        {(mergeAll.error ?? catchUp.error ?? runChecks.error) && <p className="error">{(mergeAll.error ?? catchUp.error ?? runChecks.error)?.message}</p>}
      </div>
    </Dialog>
  )
}

const RESULT_LABEL: Record<MergeOutcome['result'], string> = {
  Merged: 'Merged',
  Conflict: 'Conflicts',
  Failed: 'Not merged',
  NotTried: 'Not tried',
}
