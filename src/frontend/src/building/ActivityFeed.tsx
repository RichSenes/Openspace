import { Activity, ChevronUp } from 'lucide-react'
import { useState } from 'react'
import { useFeed } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { byDay, FEED_MAX, FEED_PAGE, feedKind, feedPlace, feedText, feedTime, moreLines } from '../activity/feed'

interface Props {
  office: OfficeView
  /** Opens a task on the board of its own office. */
  onOpenTask: (taskId: string, officeId: string) => void
}

const OPEN_KEY = 'openspace.feed.open'

/**
 * What happens in the building, live, at the top right: closed it shows the latest line,
 * open it lists the latest ones for this office, one of its rooms or every office.
 */
export function ActivityFeed({ office, onOpenTask }: Props) {
  const [open, setOpen] = useState(() => localStorage.getItem(OPEN_KEY) === 'true')
  const [everyOffice, setEveryOffice] = useState(false)
  const [roomId, setRoomId] = useState('')
  const [everything, setEverything] = useState(false)
  const [limit, setLimit] = useState(FEED_PAGE)
  const feed = useFeed({ officeId: everyOffice ? null : office.id, roomId: roomId || null, everything, limit })
  const lines = feed.data ?? []
  // Days read against when the lines arrived: Today, Yesterday, then dates.
  const now = new Date(feed.dataUpdatedAt)

  const toggle = () => {
    localStorage.setItem(OPEN_KEY, String(!open))
    setOpen(!open)
  }

  if (!open) {
    const latest = lines[0]
    return (
      <button type="button" className="feed-toggle" aria-expanded={false} onClick={toggle}>
        <Activity size={16} aria-hidden />
        <span className="pixel">Activity</span>
        <span className="feed-toggle-latest">{latest ? `${feedPlace(latest, everyOffice)}: ${feedText(latest)}` : 'Nothing yet'}</span>
      </button>
    )
  }

  return (
    <section className="feed" aria-label="Activity">
      <header className="feed-head">
        <h2 className="pixel">Activity</h2>
        <button type="button" className="btn btn-quiet" aria-expanded onClick={toggle}>
          <ChevronUp size={16} aria-hidden /> Hide activity
        </button>
      </header>
      <div className="feed-filters">
        <label className="sr-only" htmlFor="feed-scope">
          Offices to follow
        </label>
        <select
          id="feed-scope"
          className="input"
          value={everyOffice ? 'all' : 'this'}
          onChange={(e) => {
            setEveryOffice(e.target.value === 'all')
            setRoomId('')
          }}
        >
          <option value="this">{office.name}</option>
          <option value="all">Every office</option>
        </select>
        {!everyOffice && (
          <>
            <label className="sr-only" htmlFor="feed-room">
              Room to follow
            </label>
            <select id="feed-room" className="input" value={roomId} onChange={(e) => setRoomId(e.target.value)}>
              <option value="">All rooms</option>
              {office.rooms.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </select>
          </>
        )}
        <label className="feed-check">
          <input type="checkbox" checked={everything} onChange={(e) => setEverything(e.target.checked)} /> Reads and searches
        </label>
      </div>

      {feed.error ? (
        <p className="error">The activity could not be loaded: {feed.error.message}</p>
      ) : lines.length === 0 ? (
        <p className="hint feed-empty">
          {feed.isPending
            ? 'Loading the activity…'
            : 'Nothing happened here yet. When agents work, their steps, changes, commands and notes show up here as they happen.'}
        </p>
      ) : (
        <div className="feed-lines">
          {byDay(lines, now).map((group) => (
            <section key={group.day} className="feed-day" aria-label={group.day}>
              <h3 className="feed-day-name pixel">{group.day}</h3>
              <ol className="feed-day-lines">
                {group.lines.map((line) => (
                  <li key={line.id}>
                    <button type="button" className="feed-line" onClick={() => onOpenTask(line.taskId, line.officeId)}>
                      <time className="feed-time mono" dateTime={line.at}>
                        {feedTime(line.at)}
                      </time>
                      <span className={line.succeeded === false ? 'feed-kind feed-failed' : 'feed-kind'}>{feedKind(line)}</span>
                      <span className="feed-summary">{feedText(line)}</span>
                      <span className="feed-where hint">
                        {feedPlace(line, everyOffice)} · {line.taskTitle}
                      </span>
                    </button>
                  </li>
                ))}
              </ol>
            </section>
          ))}
        </div>
      )}
      {lines.length >= limit && limit < FEED_MAX && (
        <button type="button" className="btn" onClick={() => setLimit(moreLines(limit))}>
          Show older
        </button>
      )}
    </section>
  )
}
