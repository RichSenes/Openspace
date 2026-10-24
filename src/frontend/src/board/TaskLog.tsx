import { useState } from 'react'
import { Download } from 'lucide-react'
import { useTaskLog } from '../api/hooks'
import { filterLog, KIND_LABEL, type LogFilter } from './auditLabels'

const time = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })

/** Everything that happened on a task, newest first, with the evidence one click away. */
export function TaskLog({ taskId }: { taskId: string }) {
  const log = useTaskLog(taskId, true)
  const [filter, setFilter] = useState<LogFilter>('changes')
  const entries = filterLog(log.data ?? [], filter).toReversed()

  return (
    <section className="log" aria-label="Audit log">
      <div className="log-head">
        <div className="segmented" role="group" aria-label="Show">
          <button type="button" aria-pressed={filter === 'changes'} onClick={() => setFilter('changes')}>
            Changes
          </button>
          <button type="button" aria-pressed={filter === 'all'} onClick={() => setFilter('all')}>
            Everything
          </button>
        </div>
        <a className="btn" href={`/api/tasks/${taskId}/report`} download>
          <Download size={16} /> Download report
        </a>
      </div>

      {log.isPending ? (
        <p className="hint">Reading the log…</p>
      ) : log.error ? (
        <p className="error">Could not read the log: {log.error.message}</p>
      ) : entries.length === 0 ? (
        <p className="hint">
          {filter === 'changes'
            ? 'Nothing changed yet. Switch to Everything to see what the agent read and searched.'
            : 'Nothing logged yet.'}
        </p>
      ) : (
        <ol className="log-list">
          {entries.map((e) => (
            <li key={e.id} className={`log-entry log-${e.kind.toLowerCase()}`}>
              <span className="log-time mono">{time(e.at)}</span>
              <span className="log-kind pixel">{KIND_LABEL[e.kind]}</span>
              <div className="log-body">
                {e.detail ? (
                  <details>
                    <summary>
                      {e.summary}
                      {e.succeeded === false && <span className="log-failed"> · failed</span>}
                    </summary>
                    <pre className="mono">{e.detail}</pre>
                  </details>
                ) : (
                  <p>
                    {e.summary}
                    {e.succeeded === false && <span className="log-failed"> · failed</span>}
                  </p>
                )}
              </div>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}
