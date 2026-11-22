import { useMemo, useState, type MouseEvent, type ReactNode } from 'react'
import DOMPurify from 'dompurify'
import { ArrowLeft, History, Pencil } from 'lucide-react'
import { formatDay, formatMoment } from '../ui/dates'
import { useToast } from '../ui/toastContext'
import { useBacklinks, useHistory, useNote, useNoteText, useSaveNote, useVersion } from './api'
import { Badges } from './Badges'
import { diffStats, parseDiff, shortCommit, withoutHeaders } from './diff'
import { editRefusal, isConstitution, renderNote, resolveLink, reviewState, withoutTitle } from './wiki'

interface Props {
  path: string
  /** Every note's path, to resolve the links of this one. */
  paths: string[]
  onOpen: (path: string) => void
  /** On narrow screens the note replaces the tree; this goes back to it. */
  onBack?: () => void
}

/** One note: what it is, its text with links that open other notes, who links here, its history. */
export function NotePane({ path, paths, onOpen, onBack }: Props) {
  const note = useNote(path)
  const [editing, setEditing] = useState(false)
  const [showHistory, setShowHistory] = useState(false)
  const refusal = editRefusal(path)

  if (note.isPending) {
    return (
      <section className="brain-note cut-lg">
        <Back onBack={onBack} />
        <p className="hint">Opening {path}…</p>
      </section>
    )
  }
  if (note.error || !note.data) {
    return (
      <section className="brain-note cut-lg">
        <Back onBack={onBack} />
        <h2>Note not found</h2>
        <p className="hint">{path} is not in the brain any more. Pick another note.</p>
      </section>
    )
  }

  const n = note.data
  return (
    <article className="brain-note cut-lg" aria-labelledby="brain-note-title">
      <Back onBack={onBack} />
      <header className="brain-note-head">
        <div className="brain-note-title">
          <h2 id="brain-note-title">{n.title}</h2>
          <Badges kind={n.kind} status={n.status} />
        </div>
        <p className="brain-note-path mono">{n.path}</p>
        <p className="hint">
          Changed {formatMoment(n.modified)}
          {n.author ? ` · by ${n.author}` : ''}
          {n.from ? ` · from ${n.from}` : ''}
        </p>
        <div className="brain-note-actions">
          {!refusal && !editing && (
            <button type="button" className="btn" onClick={() => setEditing(true)}>
              <Pencil size={14} aria-hidden /> Edit
            </button>
          )}
          <button type="button" className="btn" aria-expanded={showHistory} onClick={() => setShowHistory(!showHistory)}>
            <History size={14} aria-hidden /> {showHistory ? 'Hide history' : 'History'}
          </button>
        </div>
        {refusal && <p className="hint">{refusal}</p>}
      </header>

      {(n.kind === 'decision' || n.review || n.paths.length > 0 || n.offices.length > 0 || n.rooms.length > 0 || n.supersedes) && (
        <dl className="brain-facts">
          {n.kind === 'decision' && (
            <Fact term="Status">{n.status ? <Badges kind={null} status={n.status} /> : <span className="hint">not set</span>}</Fact>
          )}
          {n.review && (
            <Fact term="Review">
              <span className={`brain-review brain-review-${reviewState(n.review)}`}>
                {formatDay(n.review)}
                {reviewState(n.review) === 'overdue' ? ' · due' : reviewState(n.review) === 'soon' ? ' · soon' : ''}
              </span>
            </Fact>
          )}
          {n.offices.length > 0 && <Fact term="Offices">{n.offices.join(', ')}</Fact>}
          {n.rooms.length > 0 && <Fact term="Rooms">{n.rooms.join(', ')}</Fact>}
          {n.paths.length > 0 && <Fact term="Files">{<span className="mono">{n.paths.join(', ')}</span>}</Fact>}
          {n.supersedes && (
            <Fact term="Replaces">
              <NoteBody markdown={n.supersedes} from={n.path} paths={paths} onOpen={onOpen} inline />
            </Fact>
          )}
        </dl>
      )}

      {editing ? (
        <Editor path={path} onDone={() => setEditing(false)} />
      ) : n.body.trim() ? (
        <NoteBody markdown={withoutTitle(n.body, n.title)} from={n.path} paths={paths} onOpen={onOpen} />
      ) : (
        <p className="hint">This note is empty.</p>
      )}

      {showHistory && <HistoryList path={path} />}
      <Backlinks path={path} onOpen={onOpen} />
    </article>
  )
}

function Back({ onBack }: { onBack?: () => void }) {
  if (!onBack) return null
  return (
    <button type="button" className="btn btn-quiet brain-back" onClick={onBack}>
      <ArrowLeft size={14} aria-hidden /> All notes
    </button>
  )
}

function Fact({ term, children }: { term: string; children: ReactNode }) {
  return (
    <div>
      <dt className="label">{term}</dt>
      <dd>{children}</dd>
    </div>
  )
}

/** The note's Markdown, sanitised, with [[links]] that open the note they point at. */
function NoteBody({ markdown, from, paths, onOpen, inline }: { markdown: string; from: string; paths: string[]; onOpen: (path: string) => void; inline?: boolean }) {
  const html = useMemo(() => DOMPurify.sanitize(renderNote(markdown, (t) => resolveLink(t, from, paths))), [markdown, from, paths])
  const click = (e: MouseEvent) => {
    const link = (e.target as HTMLElement).closest('a.wikilink')
    const target = link?.getAttribute('data-path')
    if (target) {
      e.preventDefault()
      onOpen(target)
    }
  }
  // The links inside are real links: Enter on them sends a click, which lands here.
  return <div className={inline ? 'markdown brain-inline' : 'markdown brain-body'} onClick={click} dangerouslySetInnerHTML={{ __html: html }} />
}

/** The whole file in a text box, front matter included, saved with a summary as the person. */
function Editor({ path, onDone }: { path: string; onDone: () => void }) {
  const source = useNoteText(path, true)
  const [text, setText] = useState<string>()
  const [summary, setSummary] = useState('')
  const save = useSaveNote(path)
  const toast = useToast()
  const value = text ?? source.data?.text ?? ''
  const changed = text !== undefined && text !== source.data?.text

  if (source.isPending) return <p className="hint">Opening the file…</p>
  if (source.error) return <p className="error">The file cannot be read: {source.error.message}</p>

  return (
    <form
      className="brain-editor"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate(
          { text: value, summary: summary.trim() || undefined },
          {
            onSuccess: () => {
              toast('Note saved')
              onDone()
            },
          },
        )
      }}
    >
      {isConstitution(path) && (
        <p className="hint">
          The constitution is yours alone: agents can only propose rules, and every change here is committed under your name.
        </p>
      )}
      <label className="field">
        <span>The whole note: front matter, then Markdown</span>
        <textarea className="input mono brain-textarea" value={value} spellCheck={false} onChange={(e) => setText(e.target.value)} />
      </label>
      <label className="field">
        <span>What changed (optional, for the history)</span>
        <input className="input" value={summary} placeholder="edited the note" onChange={(e) => setSummary(e.target.value)} />
      </label>
      {save.error && <p className="error">{save.error.message}</p>}
      <div className="brain-editor-actions">
        <button type="submit" className="btn btn-primary" disabled={!changed || save.isPending}>
          {save.isPending ? 'Saving…' : 'Save note'}
        </button>
        <button type="button" className="btn" onClick={onDone} disabled={save.isPending}>
          Cancel
        </button>
        {!changed && <span className="hint">Nothing changed yet.</span>}
      </div>
    </form>
  )
}

function HistoryList({ path }: { path: string }) {
  const history = useHistory(path)
  const [commit, setCommit] = useState<string>()
  return (
    <section className="brain-section" aria-labelledby="brain-history-title">
      <h3 id="brain-history-title" className="label">
        History
      </h3>
      {history.isPending ? (
        <p className="hint">Reading the vault's history…</p>
      ) : history.error ? (
        <p className="error">{history.error.message}</p>
      ) : history.data.length === 0 ? (
        <p className="hint">No history yet: the vault has not committed this note.</p>
      ) : (
        <ul className="brain-commits">
          {history.data.map((c) => (
            <li key={c.commit}>
              <button type="button" className="brain-commit" aria-pressed={commit === c.commit} onClick={() => setCommit(commit === c.commit ? undefined : c.commit)}>
                <span className="mono brain-commit-hash">{shortCommit(c.commit)}</span>
                <span className="brain-commit-summary">{c.summary}</span>
                <span className="hint">
                  {c.author} · {formatMoment(c.date)}
                </span>
              </button>
              {commit === c.commit && <Diff path={path} commit={c.commit} />}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function Diff({ path, commit }: { path: string; commit: string }) {
  const version = useVersion(path, commit)
  const lines = useMemo(() => (version.data ? withoutHeaders(parseDiff(version.data.diff)) : []), [version.data])
  if (version.isPending) return <p className="hint">Reading the change…</p>
  if (version.error) return <p className="error">{version.error.message}</p>
  const stats = diffStats(lines)
  return (
    <div className="brain-diff">
      <p className="hint">
        {version.data.text === null ? 'This change deleted the note. ' : ''}
        {stats.added} added, {stats.removed} removed{version.data.previous ? '' : ' · the first version'}
        {version.data.truncated ? ' · cut short, too long to show whole' : ''}
      </p>
      <div className="brain-diff-scroll">
        <table className="brain-diff-table">
          <tbody>
            {lines.map((l, i) => (
              <tr key={i} className={`brain-diff-${l.kind}`}>
                <td className="brain-diff-num">{l.old ?? ''}</td>
                <td className="brain-diff-num">{l.new ?? ''}</td>
                <td className="brain-diff-sign" aria-label={l.kind === 'add' ? 'added' : l.kind === 'del' ? 'removed' : undefined}>
                  {l.kind === 'add' ? '+' : l.kind === 'del' ? '−' : ''}
                </td>
                <td className="brain-diff-text">{l.text}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function Backlinks({ path, onOpen }: { path: string; onOpen: (path: string) => void }) {
  const backlinks = useBacklinks(path)
  return (
    <section className="brain-section" aria-labelledby="brain-backlinks-title">
      <h3 id="brain-backlinks-title" className="label">
        Linked from
      </h3>
      {backlinks.isPending ? (
        <p className="hint">Looking for links…</p>
      ) : backlinks.error ? (
        <p className="error">{backlinks.error.message}</p>
      ) : backlinks.data.length === 0 ? (
        <p className="hint">No note links here yet.</p>
      ) : (
        <ul className="brain-backlinks">
          {backlinks.data.map((b) => (
            <li key={b.path}>
              <button type="button" className="brain-hit" onClick={() => onOpen(b.path)}>
                <span className="brain-hit-title">{b.title}</span>
                <Badges kind={b.kind} status={b.status} />
                <span className="brain-hit-path mono">{b.path}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
