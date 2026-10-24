import { useState } from 'react'
import { Check, CircleSlash, GitMerge, TriangleAlert, X } from 'lucide-react'
import { useBranches, useCatchUp, useMergeAll } from '../api/hooks'
import type { MergeOutcome, OfficeView, WorkTaskView } from '../api/types'
import { Dialog } from '../ui/Dialog'
import { useToast } from '../ui/toastContext'
import type { DeskWithRoom } from './deskLookup'
import { defaultMergeTarget } from './mergeTarget'
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
  const catchUp = useCatchUp()
  const toast = useToast()
  const chosen = waiting.filter((t) => !skipped.has(t.id))
  const deskOf = (taskId: string) => desks.find((d) => d.id === tasks.find((t) => t.id === taskId)?.deskId)?.name ?? 'its agent'

  const close = () => {
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
          const merged = result.filter((o) => o.result === 'Merged').length
          toast(`${merged} of ${result.length} merged into ${target}`)
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
        {outcomes.length > 0 && (
          <section className="merge-results" aria-label="What happened">
            <h3 className="pixel notes-title">Last round into {target}</h3>
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

            <div className="row">
              <button type="button" className="btn btn-primary" disabled={!target || chosen.length === 0 || mergeAll.isPending} onClick={merge}>
                <GitMerge size={16} aria-hidden /> Merge {chosen.length} into {target || '…'}
              </button>
            </div>
            <p className="hint">
              Your checkout never moves: other branches are merged in a temporary copy. Work that conflicts is skipped and its agent can
              resolve it; with uncommitted changes on the branch nothing is touched.
            </p>
          </>
        )}
        {(mergeAll.error ?? catchUp.error) && <p className="error">{(mergeAll.error ?? catchUp.error)?.message}</p>}
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
