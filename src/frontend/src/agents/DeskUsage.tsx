import { useDeskUsage } from '../api/hooks'
import { compactTokens } from '../board/taskRules'

/** Tokens each desk's agent spent over all its tasks, the hungriest first. */
export function DeskUsage() {
  const usage = useDeskUsage()
  return (
    <section className="desk-usage" aria-labelledby="desk-usage-title">
      <h2 id="desk-usage-title">Tokens by desk</h2>
      {usage.error && <p className="error">Could not read the usage: {usage.error.message}</p>}
      {usage.data?.length === 0 && <p className="hint">Nothing counted yet: tokens show up here as agents work. Each task shows its own, round by round.</p>}
      {(usage.data?.length ?? 0) > 0 && (
        <table className="token-table">
          <thead>
            <tr>
              <th scope="col">Desk</th>
              <th scope="col">Tasks</th>
              <th scope="col">Read</th>
              <th scope="col">Written</th>
              <th scope="col">Cache</th>
            </tr>
          </thead>
          <tbody>
            {usage.data?.map((d) => (
              <tr key={d.deskId}>
                <th scope="row">
                  {d.deskName} <span className="hint">· {d.officeName}</span>
                </th>
                <td>{d.tasks}</td>
                <td>{compactTokens(d.input)}</td>
                <td>{compactTokens(d.output)}</td>
                <td>{compactTokens(d.cacheRead + d.cacheWrite)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <p className="hint">Read is what the model took in, written what it answered; cache is context it read again cheaply.</p>
    </section>
  )
}
