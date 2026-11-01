import { useCheckVersion, useVersion } from '../api/hooks'
import { useToast } from '../ui/toastContext'

/** Which version runs here, and whether a newer one is out. Openspace never updates itself. */
export function VersionCard() {
  const view = useVersion().data
  const check = useCheckVersion()
  const toast = useToast()
  if (!view) return null
  const look = () =>
    check.mutate(undefined, {
      onSuccess: (v) => toast(v.newer && v.latest ? `${v.latest.name} is out` : v.latest ? 'This is the latest version' : 'No release published yet'),
    })
  return (
    <section className="settings-card" aria-labelledby="version">
      <h2 id="version">Version</h2>
      <p>
        This is Openspace {view.current}.
        {view.newer && view.latest && (
          <>
            {' '}
            <a href={view.latest.url} target="_blank" rel="noreferrer">
              {view.latest.name}
            </a>{' '}
            is out.
          </>
        )}
      </p>
      <p className="hint">
        Openspace looks for new releases on GitHub twice a day and tells you about them; it never updates itself. To update, run{' '}
        <code className="mono">git pull</code> and <code className="mono">scripts/openspace restart</code> in its folder.
        {view.checkedAt && ` Last looked at ${new Date(view.checkedAt).toLocaleString()}.`}
      </p>
      <div className="row">
        <button type="button" className="btn" disabled={check.isPending} onClick={look}>
          Look for a new version
        </button>
      </div>
      {check.error && <p className="error">{check.error.message}</p>}
    </section>
  )
}
