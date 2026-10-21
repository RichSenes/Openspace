import type { DeskView, OfficeView } from '../api/types'

export interface DeskWithRoom extends DeskView {
  roomName: string
}

export function desksOf(office: OfficeView): DeskWithRoom[] {
  return office.rooms.flatMap((r) => r.desks.map((d) => ({ ...d, roomName: r.name })))
}
