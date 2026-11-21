import { useState } from 'react'
import { Check, ExternalLink, FileUp, ShieldCheck, Unplug } from 'lucide-react'
import { useCheckGoogle, useConnectGoogle, useDisconnectGoogle, useGoogle, useSaveGoogleClient } from '../api/hooks'
import { useToast } from '../ui/toastContext'
import { allows, clientFileProblem, googleStep } from './google'
import './connections.css'

const CONSOLE = 'https://console.cloud.google.com'

/**
 * Google accounts for routines, set up in three steps: the person's own OAuth client made in
 * Google Cloud, its file given to Openspace, then each account connected and checked. Keys stay
 * in files on this computer that only the person can read.
 */
export function GoogleCard() {
  const [waiting, setWaiting] = useState(false)
  const google = useGoogle(waiting)
  const connect = useConnectGoogle()
  const saveClient = useSaveGoogleClient()
  const toast = useToast()
  const [fileProblem, setFileProblem] = useState<string>()
  const data = google.data
  const step = googleStep(data)
  const ready = Boolean(data?.clientReady)

  const start = () =>
    connect.mutate(undefined, {
      onSuccess: ({ url }) => {
        window.open(url, '_blank', 'noopener')
        setWaiting(true)
      },
    })

  const choose = async (file: File) => {
    const text = await file.text()
    const problem = clientFileProblem(text)
    setFileProblem(problem ?? undefined)
    if (!problem) saveClient.mutate(text, { onSuccess: () => toast('Google OAuth client saved') })
  }

  return (
    <section className="settings-card" aria-labelledby="google">
      <h3 id="google">Google accounts</h3>
      <p className="hint">
        Routines read, label and archive mail, put dates in your calendar and tasks, and keep reply drafts, in the accounts connected here. They never
        send mail. Openspace uses an OAuth client of your own, so nothing passes through anyone else.
      </p>
      {google.error && <p className="error">{google.error.message}</p>}
      {data && (
        <p className="connection-status" role="status">
          {step === 'client' ? 'Not set up yet.' : step === 'account' ? 'OAuth client ready: connect an account.' : `${data.accounts.length} ${data.accounts.length === 1 ? 'account' : 'accounts'} connected.`}
        </p>
      )}
      <ol className="guide">
        <li className={ready ? 'done' : step === 'client' ? 'next' : undefined}>
          <span className="guide-mark" aria-hidden>
            {ready ? <Check size={14} /> : 1}
          </span>
          <div className="guide-body">
            <strong>Make an OAuth client in Google Cloud</strong>
            {!ready && (
              <ol>
                <li>
                  <a href={`${CONSOLE}/projectcreate`} target="_blank" rel="noreferrer">
                    Create a project
                  </a>{' '}
                  (any name; it stays yours).
                </li>
                <li>
                  Turn on the{' '}
                  <a href={`${CONSOLE}/apis/library/gmail.googleapis.com`} target="_blank" rel="noreferrer">
                    Gmail
                  </a>
                  ,{' '}
                  <a href={`${CONSOLE}/apis/library/calendar-json.googleapis.com`} target="_blank" rel="noreferrer">
                    Calendar
                  </a>{' '}
                  and{' '}
                  <a href={`${CONSOLE}/apis/library/tasks.googleapis.com`} target="_blank" rel="noreferrer">
                    Tasks
                  </a>{' '}
                  APIs in it.
                </li>
                <li>
                  In the{' '}
                  <a href={`${CONSOLE}/auth/audience`} target="_blank" rel="noreferrer">
                    audience
                  </a>{' '}
                  of the consent screen, choose External and add your own address as a test user.
                </li>
                <li>
                  In{' '}
                  <a href={`${CONSOLE}/auth/clients`} target="_blank" rel="noreferrer">
                    Clients
                  </a>
                  , create a client of type <em>Desktop app</em>, then download its JSON file.
                </li>
              </ol>
            )}
          </div>
          <span className="sr-only">{ready ? 'Done' : 'To do'}</span>
        </li>

        <li className={ready ? 'done' : step === 'client' ? 'next' : undefined}>
          <span className="guide-mark" aria-hidden>
            {ready ? <Check size={14} /> : 2}
          </span>
          <div className="guide-body">
            <strong>Give Openspace the client file</strong>
            <label className={ready ? 'btn btn-quiet' : 'btn btn-primary'}>
              <FileUp size={16} aria-hidden /> {ready ? 'Replace the client file' : 'Choose the client file'}
              <input
                type="file"
                accept=".json,application/json"
                className="sr-only"
                disabled={saveClient.isPending}
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  if (file) void choose(file)
                  e.target.value = ''
                }}
              />
            </label>
            <span className="hint">
              {ready
                ? `Kept in ${data?.folder}, readable only by you. Its secret is never shown again.`
                : `The file goes to ${data?.folder ?? 'your Openspace folder'} on this computer, readable only by you.`}
            </span>
            {fileProblem && <p className="error">{fileProblem}</p>}
            {saveClient.error && <p className="error">{saveClient.error.message}</p>}
          </div>
          <span className="sr-only">{ready ? 'Done' : 'To do'}</span>
        </li>

        <li className={step === 'done' ? 'done' : step === 'account' ? 'next' : undefined}>
          <span className="guide-mark" aria-hidden>
            {step === 'done' ? <Check size={14} /> : 3}
          </span>
          <div className="guide-body">
            <strong>Connect your accounts</strong>
            {data && data.accounts.length > 0 && (
              <ul className="google-accounts">
                {data.accounts.map((a) => (
                  <Account key={a.email} email={a.email} scopes={a.scopes} />
                ))}
              </ul>
            )}
            <button type="button" className={step === 'account' ? 'btn btn-primary' : 'btn'} disabled={!ready || connect.isPending} onClick={start}>
              <ExternalLink size={16} aria-hidden /> {data && data.accounts.length > 0 ? 'Connect another account' : 'Connect a Google account'}
            </button>
            {!ready && <span className="hint">Choose the client file first.</span>}
            {waiting && (
              <>
                <span className="hint">
                  Say yes in the tab that opened. Google warns that it has not checked the app: it is yours, so choose Advanced, then go on. The account
                  shows up here by itself.
                </span>
                <button type="button" className="btn btn-quiet" onClick={() => setWaiting(false)}>
                  Stop waiting
                </button>
              </>
            )}
            {connect.error && <p className="error">{connect.error.message}</p>}
          </div>
          <span className="sr-only">{step === 'done' ? 'Done' : 'To do'}</span>
        </li>
      </ol>
    </section>
  )
}

/** A connected account: what routines can use of it, a check that its key still works, and the way out. */
function Account({ email, scopes }: { email: string; scopes: string[] }) {
  const check = useCheckGoogle()
  const disconnect = useDisconnectGoogle()
  const toast = useToast()
  const [leaving, setLeaving] = useState(false)
  return (
    <li>
      <div>
        <strong>{email}</strong>
        <p className="hint">{allows(scopes)}</p>
        {check.error && <p className="error">{check.error.message}</p>}
      </div>
      {leaving ? (
        <span className="row">
          <button type="button" className="btn btn-quiet" onClick={() => setLeaving(false)}>
            Keep it
          </button>
          <button
            type="button"
            className="btn btn-danger"
            disabled={disconnect.isPending}
            onClick={() => disconnect.mutate(email, { onSuccess: () => toast(`${email} disconnected`) })}
          >
            <Unplug size={16} aria-hidden /> Disconnect: its routines stop working
          </button>
        </span>
      ) : (
        <span className="row">
          <button
            type="button"
            className="btn"
            disabled={check.isPending}
            onClick={() => check.mutate(email, { onSuccess: () => toast(`${email} works`) })}
          >
            <ShieldCheck size={16} aria-hidden /> {check.isPending ? 'Checking…' : 'Check the account'}
          </button>
          <button type="button" className="btn btn-quiet" onClick={() => setLeaving(true)}>
            <Unplug size={16} aria-hidden /> Disconnect…
          </button>
        </span>
      )}
    </li>
  )
}
