import { useState } from 'react'
import { Gift } from 'lucide-react'
import { useVersion } from '../api/hooks'
import { noteLines, readSnooze, releaseToTell, snoozed } from './release'
import './release.css'

const SNOOZE_KEY = 'openspace.release.snooze'

const stored = () => {
  try {
    return readSnooze(localStorage.getItem(SNOOZE_KEY))
  } catch {
    return null
  }
}

/**
 * A new version of Openspace is out: what changed, and how to update. It never updates by
 * itself; "remind me tomorrow" puts this release off for a day, a newer one shows at once.
 */
export function ReleaseBanner() {
  const view = useVersion().data
  const [snooze, setSnooze] = useState(stored)
  const [open, setOpen] = useState(false)
  const release = releaseToTell(view, snooze, new Date())
  if (!release || !view) return null

  const later = () => {
    const next = snoozed(release, new Date())
    try {
      localStorage.setItem(SNOOZE_KEY, JSON.stringify(next))
    } catch {
      // Not kept: it shows again on the next visit.
    }
    setSnooze(next)
  }

  return (
    <section className="release" aria-label="New version">
      <div className="release-head">
        <Gift size={16} aria-hidden />
        <p>
          <strong>{release.name}</strong> is out. This is {view.current}.
        </p>
        <div className="release-actions">
          <button type="button" className="btn btn-quiet" aria-expanded={open} onClick={() => setOpen(!open)}>
            {open ? 'Hide what is new' : 'What is new'}
          </button>
          <a className="btn btn-quiet" href={release.url} target="_blank" rel="noreferrer">
            Open on GitHub
          </a>
          <button type="button" className="btn" onClick={later}>
            Remind me tomorrow
          </button>
        </div>
      </div>
      {open && (
        <div className="release-notes">
          {noteLines(release.notes).map((line, i) =>
            line.kind === 'heading' ? (
              <h3 key={i}>{line.text}</h3>
            ) : line.kind === 'item' ? (
              <p key={i} className="release-item">
                {line.text}
              </p>
            ) : (
              <p key={i}>{line.text}</p>
            ),
          )}
          <p className="hint">
            Openspace never updates itself. To update, in its folder run <code className="mono">git pull</code>, then{' '}
            <code className="mono">scripts/openspace restart</code>.
          </p>
        </div>
      )}
    </section>
  )
}
