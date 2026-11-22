import { useMemo, useState, type FormEvent } from 'react'
import { ChevronDown, ChevronRight, Search } from 'lucide-react'
import { useMediaQuery } from '../ui/useMediaQuery'
import { formatDay } from '../ui/dates'
import { useBrainSearch, useBrainTree, useWhy } from './api'
import { Badges } from './Badges'
import { NotePane } from './NotePane'
import type { TreeFolder, TreeNote } from './types'
import { countNotes, filterTree, flattenTree, foldersOf, kindsOf } from './wiki'

interface Props {
  path: string | undefined
  onOpen: (path: string | undefined) => void
}

/** The wiki: the vault as a tree, a search, "why is this so?", and the open note beside them. */
export function WikiTab({ path, onOpen }: Props) {
  const narrow = useMediaQuery('(max-width: 760px)')
  const tree = useBrainTree()
  const paths = useMemo(() => (tree.data ? flattenTree(tree.data).map((n) => n.path) : []), [tree.data])
  const showSide = !narrow || !path
  const showNote = !narrow || !!path

  return (
    <div className="brain-wiki">
      {showSide && (
        <aside className="brain-side" aria-label="Notes">
          <Browse tree={tree.data} loading={tree.isPending} error={tree.error?.message} current={path} onOpen={onOpen} />
          <Why onOpen={onOpen} />
        </aside>
      )}
      {showNote &&
        (path ? (
          <NotePane key={path} path={path} paths={paths} onOpen={onOpen} onBack={narrow ? () => onOpen(undefined) : undefined} />
        ) : (
          <section className="brain-note brain-note-empty cut-lg">
            <h2>No note open</h2>
            <p className="hint">Pick a note in the tree, search the brain, or ask why a file is the way it is.</p>
          </section>
        ))}
    </div>
  )
}

function Browse({
  tree,
  loading,
  error,
  current,
  onOpen,
}: {
  tree: TreeFolder | undefined
  loading: boolean
  error: string | undefined
  current: string | undefined
  onOpen: (path: string) => void
}) {
  const [text, setText] = useState('')
  const [kind, setKind] = useState('')
  const hits = useBrainSearch(text)
  const kinds = useMemo(() => (tree ? kindsOf(flattenTree(tree)) : []), [tree])
  const shown = useMemo(() => (tree ? filterTree(tree, kind || undefined) : undefined), [tree, kind])
  const searching = text.trim().length > 0

  return (
    <section className="brain-browse">
      <div className="brain-filters">
        <label className="field brain-search">
          <span>Search the brain</span>
          <span className="brain-search-box">
            <Search size={15} aria-hidden />
            <input className="input" type="search" value={text} placeholder="Words to find" onChange={(e) => setText(e.target.value)} />
          </span>
        </label>
        <label className="field">
          <span>Kind</span>
          <select className="input" value={kind} onChange={(e) => setKind(e.target.value)} disabled={searching}>
            <option value="">Every kind</option>
            {kinds.map((k) => (
              <option key={k} value={k}>
                {k}
              </option>
            ))}
          </select>
        </label>
      </div>
      {searching ? (
        hits.error ? (
          <p className="error">{hits.error.message}</p>
        ) : !hits.data ? (
          <p className="hint">Searching…</p>
        ) : hits.data.length === 0 ? (
          <p className="hint">No note has those words.</p>
        ) : (
          <ul className="brain-hits">
            {hits.data.map((h) => (
              <li key={h.path}>
                <button type="button" className="brain-hit" aria-current={h.path === current ? 'true' : undefined} onClick={() => onOpen(h.path)}>
                  <span className="brain-hit-title">{h.title}</span>
                  <Badges kind={h.kind} status={h.status} />
                  <span className="brain-hit-path mono">{h.path}</span>
                  {h.excerpt && <span className="brain-hit-excerpt">{h.excerpt}</span>}
                </button>
              </li>
            ))}
          </ul>
        )
      ) : loading ? (
        <p className="hint">Opening the brain…</p>
      ) : error ? (
        <p className="error">The brain is unreachable: {error}</p>
      ) : shown && countNotes(shown) > 0 ? (
        <Tree root={shown} current={current} onOpen={onOpen} forceOpen={!!kind} />
      ) : (
        <p className="hint">{kind ? `No note of kind ${kind}.` : 'The brain has no notes yet.'}</p>
      )}
    </section>
  )
}

/** Folders open and close; those around the open note start open, and so does the first level. */
function Tree({ root, current, onOpen, forceOpen }: { root: TreeFolder; current: string | undefined; onOpen: (path: string) => void; forceOpen: boolean }) {
  const [open, setOpen] = useState<Set<string>>(() => new Set([...root.folders.map((f) => f.path), ...(current ? foldersOf(current) : [])]))
  const toggle = (path: string) =>
    setOpen((was) => {
      const next = new Set(was)
      if (next.has(path)) next.delete(path)
      else next.add(path)
      return next
    })
  return (
    <ul className="brain-tree" role="tree" aria-label="The vault">
      <FolderItems folder={root} depth={0} open={open} forceOpen={forceOpen} toggle={toggle} current={current} onOpen={onOpen} />
    </ul>
  )
}

function FolderItems({
  folder,
  depth,
  open,
  forceOpen,
  toggle,
  current,
  onOpen,
}: {
  folder: TreeFolder
  depth: number
  open: Set<string>
  forceOpen: boolean
  toggle: (path: string) => void
  current: string | undefined
  onOpen: (path: string) => void
}) {
  return (
    <>
      {folder.folders.map((f) => {
        const expanded = forceOpen || open.has(f.path)
        return (
          <li key={f.path} role="treeitem" aria-expanded={expanded}>
            <button type="button" className="brain-folder" style={{ paddingLeft: 6 + depth * 14 }} onClick={() => toggle(f.path)}>
              {expanded ? <ChevronDown size={14} aria-hidden /> : <ChevronRight size={14} aria-hidden />}
              <span>{f.name}</span>
              <span className="brain-count mono">{countNotes(f)}</span>
            </button>
            {expanded && (
              <ul role="group">
                <FolderItems folder={f} depth={depth + 1} open={open} forceOpen={forceOpen} toggle={toggle} current={current} onOpen={onOpen} />
              </ul>
            )}
          </li>
        )
      })}
      {folder.notes.map((n) => (
        <li key={n.path} role="treeitem" aria-selected={n.path === current}>
          <NoteItem note={n} depth={depth} current={n.path === current} onOpen={onOpen} />
        </li>
      ))}
    </>
  )
}

function NoteItem({ note, depth, current, onOpen }: { note: TreeNote; depth: number; current: boolean; onOpen: (path: string) => void }) {
  return (
    <button
      type="button"
      className="brain-leaf"
      style={{ paddingLeft: 26 + depth * 14 }}
      aria-current={current ? 'true' : undefined}
      title={note.path}
      onClick={() => onOpen(note.path)}
    >
      <span className="brain-leaf-title">{note.title}</span>
      <Badges kind={note.kind} status={note.status} />
    </button>
  )
}

/** "Why is this so?": a file of a repository in, the accepted decisions about it out. */
function Why({ onOpen }: { onOpen: (path: string) => void }) {
  const [draft, setDraft] = useState('')
  const [asked, setAsked] = useState('')
  const why = useWhy(asked)
  const submit = (e: FormEvent) => {
    e.preventDefault()
    setAsked(draft.trim())
  }
  return (
    <section className="brain-why cut-lg" aria-labelledby="brain-why-title">
      <h2 id="brain-why-title" className="label">
        Why is this so?
      </h2>
      <form onSubmit={submit} className="brain-why-form">
        <label className="field">
          <span>A file of a repository</span>
          <input className="input mono" value={draft} placeholder="src/web/cart.ts" onChange={(e) => setDraft(e.target.value)} />
        </label>
        <button type="submit" className="btn" disabled={!draft.trim()}>
          Ask
        </button>
      </form>
      {asked &&
        (why.error ? (
          <p className="error">{why.error.message}</p>
        ) : !why.data ? (
          <p className="hint">Looking…</p>
        ) : why.data.length === 0 ? (
          <p className="hint">No accepted decision covers {asked}.</p>
        ) : (
          <ul className="brain-reasons">
            {why.data.map((r) => (
              <li key={r.path}>
                <button type="button" className="brain-hit" onClick={() => onOpen(r.path)}>
                  <span className="brain-hit-title">{r.title}</span>
                  <span className="hint">
                    Decided {formatDay(r.decided)}
                    {r.general ? ' · general' : r.covering.length > 0 ? ` · covers ${r.covering.join(', ')}` : ''}
                    {r.from ? ` · from ${r.from}` : ''}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        ))}
    </section>
  )
}
