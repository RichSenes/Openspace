import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Hand } from 'lucide-react'
import { keys, useActivity, useAnswerPermission, useDecideProposal, useNeedsYou, useOffices } from '../api/hooks'
import type { NeedsYouItem } from '../api/types'
import { useToast } from '../ui/toastContext'
import { waitingDesks, answersFor } from './activity'
import { grouped, targetOf, waited, type NeedsYouTarget } from './needs'
import './needsYou.css'

/**
 * Everything that waits for the person, behind one button in the top bar, from every office:
 * agents asking, tasks to approve, to review or that failed, routines' proposals and routines
 * that failed. Nothing pops up on its own: the button says how many wait, the list opens when
 * pressed; each item opens where it is decided, and the simplest are decided right here.
 */
export function NeedsYou({ onOpen }: { onOpen: (target: NeedsYouTarget) => void }) {
  const offices = useOffices().data
  const asking = waitingDesks(useActivity().data, offices)
  const items = useNeedsYou().data ?? []
  const [open, setOpen] = useState(false)
  /** When the list was opened: how long each item waited is counted from then. */
  const [now, setNow] = useState(0)
  const answer = useAnswerPermission()
  const decide = useDecideProposal()
  const client = useQueryClient()
  const toast = useToast()
  const box = useRef<HTMLDivElement>(null)
  const count = asking.length + items.length
  const manyOffices = (offices?.length ?? 0) > 1

  // The list closes with Escape or a click anywhere else.
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    const onClick = (e: MouseEvent) => !box.current?.contains(e.target as Node) && setOpen(false)
    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onClick)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('mousedown', onClick)
    }
  }, [open])

  if (count === 0) return null

  const go = (item: NeedsYouItem) => {
    setOpen(false)
    onOpen(targetOf(item))
  }

  const say = (item: NeedsYouItem, approve: boolean) =>
    decide.mutate(
      { id: item.id, approve },
      {
        onSuccess: () => {
          void client.invalidateQueries({ queryKey: keys.needsYou })
          toast(approve ? `Yes to ${item.title}` : `No to ${item.title}`)
        },
      },
    )

  return (
    <div className="needs-you" ref={box}>
      <button type="button" className="btn needs-you-button" aria-expanded={open} aria-haspopup="dialog" onClick={() => {
          setNow(Date.now())
          setOpen(!open)
        }}>
        <Hand size={16} aria-hidden /> {count} {count === 1 ? 'needs' : 'need'} you
      </button>
      {open && (
        <section className="needs-you-panel" role="dialog" aria-label="What waits for you">
          {asking.length > 0 && (
            <div className="needs-you-group">
              <h2>Agents ask</h2>
              {asking.map((w) => (
                <div key={w.taskId} className="needs-you-card">
                  <p className="needs-you-text">
                    <strong>
                      {w.officeName} · {w.deskName}
                    </strong>
                    <span>{w.request}</span>
                    {w.detail && <code className="needs-you-detail mono">{w.detail}</code>}
                  </p>
                  <div className="needs-you-actions">
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={answer.isPending}
                      onClick={() => answer.mutate({ taskId: w.taskId, allow: true }, { onSuccess: () => toast(`${w.deskName}: ${answersFor(w.question).saidYes}`) })}
                    >
                      {answersFor(w.question).yes}
                    </button>
                    <button
                      type="button"
                      className="btn btn-danger"
                      disabled={answer.isPending}
                      onClick={() => answer.mutate({ taskId: w.taskId, allow: false }, { onSuccess: () => toast(`${w.deskName}: ${answersFor(w.question).saidNo}`) })}
                    >
                      {answersFor(w.question).no}
                    </button>
                  </div>
                </div>
              ))}
              <p className="hint">In auto mode only risky requests wait here. Change it in Settings.</p>
            </div>
          )}

          {grouped(items).map((group) => (
            <div key={group.kind} className="needs-you-group">
              <h2>
                {group.heading} <span className="needs-you-count">{group.items.length}</span>
              </h2>
              {group.items.map((item) => (
                <div key={item.id} className={item.kind === 'Proposal' ? 'needs-you-card' : 'needs-you-card needs-you-item'}>
                  <p className="needs-you-text">
                    <strong>{item.title}</strong>
                    <span className="hint">
                      {[manyOffices ? item.officeName : '', item.kind === 'Proposal' ? item.detail : '', `waiting ${waited(item.since, now)}`].filter(Boolean).join(' · ')}
                    </span>
                    {item.kind === 'RoutineFailed' && item.detail && <span className="needs-you-why">{item.detail}</span>}
                  </p>
                  <div className="needs-you-actions">
                    {item.kind === 'Proposal' && (
                      <>
                        <button type="button" className="btn btn-primary" disabled={decide.isPending} onClick={() => say(item, true)}>
                          Yes
                        </button>
                        <button type="button" className="btn" disabled={decide.isPending} onClick={() => say(item, false)}>
                          No
                        </button>
                      </>
                    )}
                    <button type="button" className={item.kind === 'Proposal' ? 'btn btn-quiet' : 'btn'} onClick={() => go(item)}>
                      {group.open}
                    </button>
                  </div>
                </div>
              ))}
            </div>
          ))}
          {(answer.error ?? decide.error) && <p className="error">{(answer.error ?? decide.error)?.message}</p>}
        </section>
      )}
    </div>
  )
}
