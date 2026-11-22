import { statusClass } from './wiki'

/** A note's kind and status, in the HUD voice. */
export function Badges({ kind, status }: { kind: string | null; status: string | null }) {
  return (
    <span className="brain-badges">
      {kind && <span className="brain-kind">{kind}</span>}
      {status && <span className={`brain-status ${statusClass(status)}`}>{status}</span>}
    </span>
  )
}
