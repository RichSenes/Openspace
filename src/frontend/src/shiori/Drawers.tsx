import { Check, Pencil, Plus, Trash2, X } from 'lucide-react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useDeleteShioriSession, useRenameShioriSession } from '../api/hooks'
import type { ShioriSessionSummary, ShioriSessionView } from '../api/types'
import { boardSections } from '../lab/shiori/board'
import { MODES } from '../lab/shiori/modes'
import { tokenize } from '../lab/shiori/script'
import { formatMoment } from '../ui/dates'
import { boardItems, modeOf, orderSessions } from './talk'
import { Words } from './Words'

function Drawer({ side, title, onClose, children }: { side: 'left' | 'right'; title: string; onClose: () => void; children: ReactNode }) {
  return (
    <aside className={`pr-drawer sp-drawer sp-drawer-${side}`} aria-label={title}>
      <div className="pr-drawer-head">
        <h2>{title}</h2>
        <button type="button" className="pr-icon" onClick={onClose} aria-label={`Close ${title.toLowerCase()}`}>
          <X size={18} />
        </button>
      </div>
      {children}
    </aside>
  )
}

interface SessionsProps {
  sessions: ShioriSessionSummary[] | undefined
  loading: boolean
  current: string | undefined
  onPick: (id: string) => void
  onNew: () => void
  onClose: () => void
}

/** Renaming a session in place: a field with its name, saved with Enter or Save. */
function RenameSession({ session, onDone }: { session: ShioriSessionSummary; onDone: () => void }) {
  const [title, setTitle] = useState(session.title)
  const rename = useRenameShioriSession()
  const save = () => {
    const name = title.trim()
    if (!name || name === session.title) return onDone()
    rename.mutate({ sessionId: session.id, title: name }, { onSuccess: onDone })
  }
  return (
    <form
      className="sp-session-edit"
      onSubmit={(e) => {
        e.preventDefault()
        save()
      }}
    >
      <label className="sr-only" htmlFor={`rename-${session.id}`}>
        Name of the session
      </label>
      <input
        id={`rename-${session.id}`}
        className="sp-session-name"
        value={title}
        maxLength={120}
        autoFocus
        onChange={(e) => setTitle(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            e.stopPropagation()
            onDone()
          }
        }}
      />
      <div className="sp-session-actions">
        <button type="submit" className="pr-btn" disabled={!title.trim() || rename.isPending}>
          <Check size={14} aria-hidden="true" />
          <span>Save</span>
        </button>
        <button type="button" className="pr-btn" onClick={onDone}>
          Cancel
        </button>
      </div>
      {rename.isError && <p className="sp-session-error">{rename.error instanceof Error ? rename.error.message : 'It could not be renamed.'}</p>}
    </form>
  )
}

/** Deleting asks first, in place: the session goes, its note stays in the brain. */
function DeleteSession({ session, onDone }: { session: ShioriSessionSummary; onDone: () => void }) {
  const remove = useDeleteShioriSession()
  return (
    <div className="sp-session-edit" role="group" aria-label={`Delete ${session.title}`}>
      <p className="sp-session-ask">
        Delete <strong>{session.title}</strong>? Its note stays in the brain.
      </p>
      <div className="sp-session-actions">
        <button type="button" className="pr-btn sp-danger" disabled={remove.isPending} onClick={() => remove.mutate(session.id, { onSuccess: onDone })}>
          <Trash2 size={14} aria-hidden="true" />
          <span>Delete</span>
        </button>
        <button type="button" className="pr-btn" autoFocus onClick={onDone}>
          Keep it
        </button>
      </div>
      {remove.isError && <p className="sp-session-error">{remove.error instanceof Error ? remove.error.message : 'It could not be deleted.'}</p>}
    </div>
  )
}

export function SessionsDrawer({ sessions, loading, current, onPick, onNew, onClose }: SessionsProps) {
  const list = orderSessions(sessions ?? [])
  const [editing, setEditing] = useState<{ id: string; what: 'rename' | 'delete' } | null>(null)
  return (
    <Drawer side="left" title="Sessions" onClose={onClose}>
      <button type="button" className="pr-btn sp-new" onClick={onNew}>
        <Plus size={16} aria-hidden="true" />
        <span>New session</span>
      </button>
      {loading ? (
        <p className="pr-muted">Reading the sessions…</p>
      ) : list.length === 0 ? (
        <p className="pr-muted pr-empty">No sessions yet. Write to Shiori below and the first one starts, named after what you say.</p>
      ) : (
        <ul className="sp-sessions">
          {list.map((s) => {
            const mode = MODES[modeOf(s.mode)]
            if (editing?.id === s.id) {
              const Edit = editing.what === 'rename' ? RenameSession : DeleteSession
              return (
                <li key={s.id} style={{ '--c': mode.hex } as React.CSSProperties}>
                  <Edit session={s} onDone={() => setEditing(null)} />
                </li>
              )
            }
            return (
              <li key={s.id} className="sp-session-row">
                <button
                  type="button"
                  className={`sp-session${s.id === current ? ' is-on' : ''}${s.endedAt ? ' is-ended' : ''}`}
                  aria-current={s.id === current ? 'true' : undefined}
                  onClick={() => onPick(s.id)}
                  style={{ '--c': mode.hex } as React.CSSProperties}
                >
                  <span className="sp-session-title">{s.title}</span>
                  <span className="sp-session-meta">
                    {s.endedAt ? <span className="sp-tag">Ended, read only</span> : <span className="sp-tag sp-tag-mode">{mode.label}</span>}
                    {s.answering && <span className="sp-tag sp-tag-mode">Answering</span>}
                    <span>
                      {s.messages} {s.messages === 1 ? 'message' : 'messages'} · {formatMoment(s.updatedAt)}
                    </span>
                  </span>
                </button>
                <div className="sp-session-tools">
                  <button type="button" className="pr-icon" onClick={() => setEditing({ id: s.id, what: 'rename' })} aria-label={`Rename ${s.title}`} title="Rename">
                    <Pencil size={15} />
                  </button>
                  <button type="button" className="pr-icon" onClick={() => setEditing({ id: s.id, what: 'delete' })} aria-label={`Delete ${s.title}`} title="Delete">
                    <Trash2 size={15} />
                  </button>
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </Drawer>
  )
}

export function ConversationDrawer({ view, draft, onClose }: { view: ShioriSessionView | undefined; draft: string; onClose: () => void }) {
  const entries = view?.transcript ?? []
  // Opens on the latest words, and follows them as they come.
  const log = useRef<HTMLOListElement>(null)
  useEffect(() => {
    log.current?.lastElementChild?.scrollIntoView?.({ block: 'end' })
  }, [entries.length, draft])
  return (
    <Drawer side="left" title="Conversation" onClose={onClose}>
      {entries.length === 0 ? (
        <p className="pr-muted pr-empty">Nothing said yet. What you write and what Shiori answers stays here, and in the session's note.</p>
      ) : (
        <ol className="pr-log" ref={log}>
          {entries.map((e, i) =>
            e.role === 'Person' ? (
              <li key={i} className="pr-log-you">
                <span className="pr-who">You · {formatMoment(e.at)}</span>
                <span className="sp-pre">{e.text}</span>
              </li>
            ) : e.role === 'Shiori' ? (
              <li key={i} className="pr-log-shiori">
                <span className="pr-who">Shiori</span>
                <Words tokens={tokenize(e.text)} streaming={false} />
              </li>
            ) : e.role === 'Failed' ? (
              <li key={i} className="sp-log-failed">
                <span className="pr-who">Shiori could not answer</span>
                {e.text}
              </li>
            ) : (
              <li key={i} className="pr-log-mode" style={{ '--c': e.role === 'Ended' ? 'var(--muted)' : MODES[modeOf(e.mode)].hex } as React.CSSProperties}>
                {e.role === 'Ended' ? 'Session ended' : `${MODES[modeOf(e.mode)].label} · you switched`}
              </li>
            ),
          )}
          {view?.answering && (
            <li className="pr-log-shiori">
              <span className="pr-who">Shiori</span>
              {draft ? <Words tokens={tokenize(draft)} streaming /> : <span className="pr-muted">reading…</span>}
            </li>
          )}
        </ol>
      )}
    </Drawer>
  )
}

export function BoardDrawer({ view, onClose }: { view: ShioriSessionView | undefined; onClose: () => void }) {
  const items = boardItems(view?.board ?? [])
  const byId = new Map(items.map((i) => [i.id, i]))
  const sections = boardSections(items)
  return (
    <Drawer side="right" title="Board" onClose={onClose}>
      {sections.length === 0 ? (
        <p className="pr-muted pr-empty">Nothing on the board yet. Ideas, doubts, decisions and tasks land here as you talk.</p>
      ) : (
        <div className="pr-board">
          {sections.map((s) => (
            <section key={s.kind} className={`pr-board-sec k-${s.kind}`}>
              <h3>
                {s.heading} <span className="pr-count">{s.items.length}</span>
              </h3>
              <ul>
                {s.items.map((i) => {
                  const full = byId.get(i.id)
                  return (
                    <li key={i.id} className={full?.done ? 'sp-done' : undefined}>
                      {full?.done && <Check size={13} aria-label="Done" className="sp-done-mark" />}
                      {i.text}
                      {i.office && <span className="pr-office">{i.office}</span>}
                      {full?.note && (
                        <span className="pr-cite sp-cite sp-board-note" title={full.note}>
                          proposed · {full.note}
                        </span>
                      )}
                    </li>
                  )
                })}
              </ul>
            </section>
          ))}
        </div>
      )}
      {view && !view.endedAt && <p className="pr-muted">When the session ends, its decisions become proposals for you to approve and the board goes into {view.note}.</p>}
    </Drawer>
  )
}
