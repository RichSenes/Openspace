import { useMemo } from 'react'
import { activityByDesk, isActive } from '../activity/activity'
import { useActivity, useOfficeTasks } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { BuildingCanvas } from './BuildingCanvas'
import './building.css'

interface Props {
  office: OfficeView
  onOpenRooms: () => void
}

export function BuildingView({ office, onOpenRooms }: Props) {
  const tasks = useOfficeTasks(office.id)
  const activity = useActivity()
  const bubbles = useMemo(() => activityByDesk(activity.data, office.id), [activity.data, office.id])
  const busy = useMemo(() => {
    const running = (tasks.data ?? []).filter((t) => t.status === 'Running' && t.deskId).map((t) => t.deskId!)
    const active = [...bubbles.values()].filter(isActive).map((a) => a.deskId)
    return new Set([...running, ...active])
  }, [tasks.data, bubbles])

  if (office.rooms.length === 0) {
    return (
      <div className="empty">
        <h2>{office.name} is an empty floor</h2>
        <p>Open a room for each area of competence, then add the desks of the agents who work there.</p>
        <button type="button" className="btn btn-primary" onClick={onOpenRooms}>
          Set up rooms
        </button>
      </div>
    )
  }

  return (
    <div className="building">
      <BuildingCanvas office={office} busyDeskIds={busy} activities={bubbles} />
    </div>
  )
}
