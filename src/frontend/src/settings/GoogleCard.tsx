import { useState } from 'react'
import { ExternalLink, Unplug } from 'lucide-react'
import { useConnectGoogle, useDisconnectGoogle, useGoogle } from '../api/hooks'
import { useToast } from '../ui/toastContext'
import { allows } from './google'

/** Google accounts for routines: connected through the person's own OAuth client, keys kept on this computer. */
export function GoogleCard() {
  const [waiting, setWaiting] = useState(false)
  const google = useGoogle(waiting)
  const connect = useConnectGoogle()
  const disconnect = useDisconnectGoogle()
  const toast = useToast()
  const [leaving, setLeaving] = useState<string>()
  const data = google.data

  const start = () =>
    connect.mutate(undefined, {
      onSuccess: ({ url }) => {
        window.open(url, '_blank', 'noopener')
        setWaiting(true)
      },
    })

  return (
    <section className="settings-card" aria-labelledby="google">
      <h2 id="google">Google accounts</h2>
      <p className="hint">
        Routines read, label and archive mail, and keep reply drafts, in the accounts connected here. They never send mail. Each account&apos;s key stays
        in a file on this computer that only you can read.
      </p>
      {google.error && <p className="error">{google.error.message}</p>}
      {data && !data.clientReady && (
        <p>
          First put your Google Cloud project&apos;s OAuth client (a desktop app) in <code className="mono">{data.folder}/google-client.json</code>.
        </p>
      )}
      {data && data.accounts.length === 0 && data.clientReady && <p>No account connected yet.</p>}
      {data && data.accounts.length > 0 && (
        <ul className="google-accounts">
          {data.accounts.map((a) => (
            <li key={a.email}>
              <div>
                <strong>{a.email}</strong>
                <p className="hint">{allows(a.scopes)}</p>
              </div>
              {leaving === a.email ? (
                <span className="row">
                  <button type="button" className="btn btn-quiet" onClick={() => setLeaving(undefined)}>
                    Keep it
                  </button>
                  <button
                    type="button"
                    className="btn btn-danger"
                    disabled={disconnect.isPending}
                    onClick={() => disconnect.mutate(a.email, { onSuccess: () => toast(`${a.email} disconnected`) })}
                  >
                    <Unplug size={16} /> Disconnect: its routines stop working
                  </button>
                </span>
              ) : (
                <button type="button" className="btn btn-quiet" onClick={() => setLeaving(a.email)}>
                  <Unplug size={16} /> Disconnect…
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      <div className="row">
        <button type="button" className="btn" disabled={!data?.clientReady || connect.isPending} onClick={start}>
          <ExternalLink size={16} /> Connect a Google account
        </button>
        {waiting && (
          <button type="button" className="btn btn-quiet" onClick={() => setWaiting(false)}>
            Done
          </button>
        )}
      </div>
      {waiting && (
        <p className="hint">
          Say yes in the tab that opened. Google warns that it has not checked the app: it is yours, so choose Advanced, then go on. The account shows up
          here by itself.
        </p>
      )}
      {connect.error && <p className="error">{connect.error.message}</p>}
    </section>
  )
}
