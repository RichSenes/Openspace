import { useState, type FormEvent } from 'react'
import { Copy, Download, FileText } from 'lucide-react'
import { useEpics, useReports, useWriteReport } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { useToast } from '../ui/toastContext'
import { usePersistentState } from '../usePersistentState'
import { Markdown } from './Markdown'
import { dayLabel, dayRange, LANGUAGES, today, type LanguageName } from './period'
import './reports.css'

const when = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/** Reports for people who were not there: of a day of the office, or of an epic. */
export function ReportsPanel({ office }: { office: OfficeView }) {
  const reports = useReports(office.id)
  const epics = useEpics(office.id)
  const write = useWriteReport(office.id)
  const toast = useToast()
  const [language, setLanguage] = usePersistentState<LanguageName>('openspace.report-language', 'Italian')
  const [date, setDate] = useState(today())
  const [epicId, setEpicId] = useState('')
  const [openId, setOpenId] = useState<string>()
  const open = reports.data?.find((r) => r.id === openId) ?? reports.data?.[0]

  const writeDay = (e: FormEvent) => {
    e.preventDefault()
    write.mutate(
      { kind: 'day', ...dayRange(date), scope: dayLabel(date, language), language },
      { onSuccess: (r) => { setOpenId(r.id); toast('Report written') } },
    )
  }

  const writeEpic = (e: FormEvent) => {
    e.preventDefault()
    write.mutate({ kind: 'epic', epicId, language }, { onSuccess: (r) => { setOpenId(r.id); toast('Report written') } })
  }

  const copy = async () => {
    if (!open) return
    try {
      await navigator.clipboard.writeText(open.body)
      toast('Copied: paste it in an email or a chat')
    } catch {
      toast('Copying is not allowed here: use Download instead')
    }
  }

  return (
    <section className="reports">
      <header className="reports-head">
        <h1>Reports of {office.name}</h1>
        <p className="hint">Written from the audit log for people who were not there. The AI only rewords what Openspace recorded.</p>
      </header>

      <div className="report-forms">
        <label className="field">
          <span>Language</span>
          <select className="input" value={language} onChange={(e) => setLanguage(e.target.value as LanguageName)}>
            {LANGUAGES.map((l) => (
              <option key={l.name} value={l.name}>
                {l.label}
              </option>
            ))}
          </select>
        </label>
        <form className="report-form" onSubmit={writeDay}>
          <label className="field">
            <span>A day of work</span>
            <input className="input" type="date" value={date} max={today()} onChange={(e) => setDate(e.target.value)} required />
          </label>
          <button type="submit" className="btn btn-primary" disabled={write.isPending}>
            <FileText size={16} /> Write day report
          </button>
        </form>
        <form className="report-form" onSubmit={writeEpic}>
          <label className="field">
            <span>A request to the supervisor</span>
            <select className="input" value={epicId} onChange={(e) => setEpicId(e.target.value)} required>
              <option value="">Choose an epic</option>
              {epics.data?.map((ep) => (
                <option key={ep.id} value={ep.id}>
                  {ep.title}
                </option>
              ))}
            </select>
          </label>
          <button type="submit" className="btn" disabled={write.isPending || !epicId}>
            <FileText size={16} /> Write epic report
          </button>
        </form>
      </div>
      {write.isPending && <p className="hint">The reporter is reading the log… this can take a minute.</p>}
      {write.error && <p className="error">{write.error.message}</p>}

      {reports.data?.length === 0 ? (
        <p className="hint">No reports yet. Write the first one above.</p>
      ) : (
        <div className="reports-body">
          <ul className="report-list" aria-label="Reports">
            {reports.data?.map((r) => (
              <li key={r.id}>
                <button type="button" className="report-item" aria-current={r.id === open?.id ? 'true' : undefined} onClick={() => setOpenId(r.id)}>
                  <span className="report-title">{r.title}</span>
                  <span className="hint">
                    {when(r.createdAt)} · {r.kind === 'Epic' ? 'Epic' : 'Day'}
                    {!r.writtenByAi && ' · facts only'}
                  </span>
                </button>
              </li>
            ))}
          </ul>
          {open && (
            <article className="report">
              <header className="report-actions">
                <span className="hint">
                  Written {when(open.createdAt)} for {open.requestedBy}
                  {!open.writtenByAi && '. The AI was not available: this lists the recorded facts.'}
                </span>
                <div className="row">
                  <button type="button" className="btn" onClick={copy}>
                    <Copy size={16} /> Copy text
                  </button>
                  <a className="btn" href={`/api/reports/${open.id}/download`} download>
                    <Download size={16} /> Download
                  </a>
                </div>
              </header>
              <Markdown text={open.body} />
            </article>
          )}
        </div>
      )}
    </section>
  )
}
