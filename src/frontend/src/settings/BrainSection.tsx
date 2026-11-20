import { useState, type FormEvent } from 'react'
import { ExternalLink } from 'lucide-react'
import { useBrainSummary, useMoveBrain } from '../api/hooks'
import { formatMoment } from '../ui/dates'
import { useToast } from '../ui/toastContext'
import { kindRows, obsidianLink } from './brain'

/** Shiori's vault: a folder of Markdown notes the agents read and write, open in Obsidian too. */
export function BrainSection() {
  const summary = useBrainSummary()
  if (summary.error) return <p className="error">Could not read the brain: {summary.error.message}</p>
  if (!summary.data) return <p className="hint">Reading the brain…</p>
  const s = summary.data
  return (
    <div className="settings-cards">
      <section className="settings-card" aria-labelledby="brain-vault">
        <h3 id="brain-vault">The vault</h3>
        <p className="hint">
          Shiori keeps the rules, decisions, plans, studies and the agents&apos; journals here, as Markdown notes. Agents read it before they
          work; every change is a commit in the vault&apos;s own git, never pushed anywhere.
        </p>
        <p className="mono brain-folder">{s.folder}</p>
        <div className="row">
          <a className="btn" href={obsidianLink(s.folder)}>
            <ExternalLink size={16} aria-hidden /> Open in Obsidian
          </a>
        </div>
        <p className="hint">Obsidian adds it as a vault the first time. Notes changed there are read again at once.</p>
        <MoveFolder key={s.configured} configured={s.configured} moved={s.configured !== s.folder} />
      </section>
      <section className="settings-card" aria-labelledby="brain-holds">
        <h3 id="brain-holds">What it holds</h3>
        {s.notes === 0 ? (
          <p className="hint">Nothing yet.</p>
        ) : (
          <dl className="accounts">
            {kindRows(s.kinds).map((r) => (
              <div key={r.kind} className="brain-kind">
                <dt>{r.label}</dt>
                <dd>{r.count}</dd>
              </div>
            ))}
          </dl>
        )}
        <p className="hint">
          {s.notes} {s.notes === 1 ? 'note' : 'notes'}
          {s.lastChange && `, last changed ${formatMoment(s.lastChange)}`}.
        </p>
      </section>
    </div>
  )
}

function MoveFolder({ configured, moved }: { configured: string; moved: boolean }) {
  const [folder, setFolder] = useState(configured)
  const move = useMoveBrain()
  const toast = useToast()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    move.mutate(folder, { onSuccess: () => toast('Folder kept: Openspace uses it from its next start') })
  }
  return (
    <form className="stack" onSubmit={submit}>
      <label className="field">
        <span>Folder</span>
        <input className="input mono" value={folder} onChange={(e) => setFolder(e.target.value)} required />
        <span className="hint">
          An absolute folder, or one starting with ~. Openspace opens it at its next start and creates what is missing there; it moves no
          notes.
        </span>
      </label>
      {moved && <p className="hint">From the next start: {configured}</p>}
      <div className="row">
        <button type="submit" className="btn" disabled={folder.trim() === configured || move.isPending}>
          Keep this folder
        </button>
      </div>
      {move.error && <p className="error">{move.error.message}</p>}
    </form>
  )
}
