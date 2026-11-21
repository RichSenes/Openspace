import { FolderOpen, NotebookPen, Undo2, X } from 'lucide-react'
import { useMemo } from 'react'
import { KIND_LABELS, noteFromBoard, type BoardItem, type BoardKind } from './board'
import { KIND_ICONS } from './icons'
import { SESSION_DATE, SESSION_TITLE } from './script'
import { Cite } from './Transcript'

/** On the board items keep the order they came in, the way a session goes. */
const BOARD_ORDER: BoardKind[] = ['idea', 'doubt', 'decision', 'task']

export type BoardPhase = 'board' | 'folding' | 'note'

/** A rough rectangle path: four slightly bent strokes that overshoot at the corners. */
function roughRect(seed: number) {
  const j = (k: number) => (((seed * 9301 + k * 49297) % 233280) / 233280 - 0.5) * 2.4
  return `M${1 + j(1)} ${2 + j(2)} Q50 ${j(3)} ${99 + j(4)} ${1.5 + j(5)} Q${100 + j(6)} 50 ${98.5 + j(7)} ${98 + j(8)} Q50 ${100 + j(9)} ${1.5 + j(10)} ${98.5 + j(11)} Q${j(12)} 50 ${2 + j(13)} ${0.5 + j(14)}`
}

function Item({ item, index }: { item: BoardItem; index: number }) {
  const Icon = KIND_ICONS[item.kind]
  const words = item.text.split(' ')
  const seed = Number(item.id.replace(/\D/g, '')) || index + 1
  return (
    <li
      className={`bi bi-${item.kind}`}
      style={{ '--i': index, '--n': words.length, '--tilt': `${((seed % 5) - 2) * 0.35}deg` } as React.CSSProperties}
    >
      <svg className="bi-frame" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
        <path pathLength={1} d={roughRect(seed)} />
      </svg>
      <span className="bi-kind">
        <Icon size={13} strokeWidth={2.2} aria-hidden="true" />
        {KIND_LABELS[item.kind].one}
        {item.office && <span className="bi-office">{item.office}</span>}
      </span>
      <p className="bi-text">
        {words.map((w, i) => (
          <span key={i} className="bi-word" style={{ '--w': i } as React.CSSProperties}>
            {w}{' '}
          </span>
        ))}
      </p>
      {item.kind === 'decision' && (
        <svg className="bi-mark" viewBox="0 0 200 60" preserveAspectRatio="none" aria-hidden="true">
          <path pathLength={1} d="M12 34 C 20 6, 182 2, 192 26 C 200 50, 30 60, 8 42 C 0 34, 14 18, 60 12" />
        </svg>
      )}
      <span className="bi-pen" aria-hidden="true" />
    </li>
  )
}

interface Props {
  items: BoardItem[]
  done: boolean
  links: string[]
  phase: BoardPhase
  onFold: () => void
  onUnfold: () => void
  /** On phones the board is a panel that opens over the chat. */
  onClose?: () => void
}

export function Whiteboard({ items, done, links, phase, onFold, onUnfold, onClose }: Props) {
  const note = useMemo(() => noteFromBoard(items, SESSION_TITLE, SESSION_DATE, links), [items, links])
  const groups = BOARD_ORDER.map((kind) => ({ kind, items: items.filter((i) => i.kind === kind) })).filter((g) => g.items.length)
  const order = new Map(groups.flatMap((g) => g.items).map((it, i) => [it.id, i]))

  return (
    <section className={`board is-${phase}`} aria-label="Whiteboard">
      <header className="board-head">
        <h2>{phase === 'note' ? 'Session note' : 'Whiteboard'}</h2>
        <span className="board-count">
          {phase === 'note' ? 'Saved' : items.length === 0 ? 'Empty' : `${items.length} ${items.length === 1 ? 'item' : 'items'}`}
        </span>
        {onClose && (
          <button type="button" className="btn btn-quiet board-close" onClick={onClose} aria-label="Close the whiteboard">
            <X size={16} />
          </button>
        )}
      </header>

      {phase !== 'note' ? (
        <>
          <div className="board-surface">
            {items.length === 0 ? (
              <p className="board-empty">Shiori writes ideas, doubts, decisions and tasks here while you talk.</p>
            ) : (
              groups.map((g) => (
                <div key={g.kind} className={`board-group board-group-${g.kind}`}>
                  <h3>{g.items.length === 1 ? KIND_LABELS[g.kind].one : KIND_LABELS[g.kind].many}</h3>
                  <ul>
                    {g.items.map((it) => (
                      <Item key={it.id} item={it} index={order.get(it.id) ?? 0} />
                    ))}
                  </ul>
                </div>
              ))
            )}
          </div>
          <footer className="board-foot">
            {done ? (
              <button type="button" className="btn btn-primary fold-btn" onClick={onFold} disabled={phase === 'folding'}>
                <NotebookPen size={16} aria-hidden="true" /> Turn into a note
              </button>
            ) : (
              <p className="hint">At the end of the session the board becomes a note.</p>
            )}
          </footer>
        </>
      ) : (
        <article className="note-card" aria-label={note.title}>
          <span className="note-fold" aria-hidden="true" />
          <p className="note-path mono">
            <FolderOpen size={13} aria-hidden="true" /> {note.path}
          </p>
          <h3 className="note-title">{note.title}</h3>
          <p className="note-date">Session of 21 November 2026 · with Shiori</p>
          {note.sections.map((s) => {
            const Icon = KIND_ICONS[s.kind]
            return (
              <section key={s.kind} className={`note-sec note-sec-${s.kind}`}>
                <h4>
                  <Icon size={13} aria-hidden="true" /> {s.heading}
                </h4>
                <ul>
                  {s.items.map((i) => (
                    <li key={i.id} className={i.kind === 'task' ? 'is-task' : undefined}>
                      {i.text}
                      {i.office && <span className="bi-office">{i.office}</span>}
                    </li>
                  ))}
                </ul>
              </section>
            )
          })}
          {note.links.length > 0 && (
            <section className="note-sec">
              <h4>Sources</h4>
              <div className="note-links">
                {note.links.map((l) => (
                  <Cite key={l} path={l} />
                ))}
              </div>
            </section>
          )}
          <footer className="note-foot">
            <button type="button" className="btn btn-quiet" onClick={onUnfold}>
              <Undo2 size={15} aria-hidden="true" /> Back to the board
            </button>
          </footer>
        </article>
      )}
    </section>
  )
}
