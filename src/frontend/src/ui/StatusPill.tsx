import type { TaskStatus } from '../api/types'
import './status.css'

export function StatusPill({ status }: { status: TaskStatus }) {
  return <span className={`status status-${status.toLowerCase()}`}>{status}</span>
}
