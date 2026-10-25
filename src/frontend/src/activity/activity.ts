import type { DeskActivity, OfficeView } from '../api/types'

/** Replaces the desk's previous activity with the new one, keeping the list in arrival order. */
export function mergeActivity(list: DeskActivity[] | undefined, update: DeskActivity): DeskActivity[] {
  return [...(list ?? []).filter((a) => a.deskId !== update.deskId), update]
}

/** The latest activity per desk of one office. */
export function activityByDesk(list: DeskActivity[] | undefined, officeId: string): Map<string, DeskActivity> {
  return new Map((list ?? []).filter((a) => a.officeId === officeId).map((a) => [a.deskId, a]))
}

/** A desk's screen is lit while its agent works or waits for you. */
export const isActive = (activity: DeskActivity | undefined) =>
  activity !== undefined && activity.kind !== 'Stopped'

export interface WaitingDesk {
  taskId: string
  officeName: string
  deskName: string
  request: string
  /** Exactly what is asked, such as the command. */
  detail: string | null
  /** The agent asks you to do or decide something, not for a permission. */
  question: boolean
}

/** Every desk, in any office, blocked until a person answers. */
export function waitingDesks(list: DeskActivity[] | undefined, offices: OfficeView[] | undefined): WaitingDesk[] {
  const desks = new Map(
    (offices ?? []).flatMap((o) => o.rooms.flatMap((r) => r.desks.map((d) => [d.id, { officeName: o.name, deskName: d.name }] as const))),
  )
  return (list ?? [])
    .filter((a) => a.kind === 'Waiting')
    .map((a) => ({
      taskId: a.taskId,
      officeName: desks.get(a.deskId)?.officeName ?? 'An office',
      deskName: desks.get(a.deskId)?.deskName ?? 'A desk',
      request: a.headline,
      detail: a.detail ?? null,
      question: Boolean(a.question),
    }))
}

/** The two answers a waiting desk gets: to a permission, or to a question the agent asked you. */
export function answersFor(question: boolean | undefined) {
  return question
    ? { yes: 'Done, go on', no: 'Stop', saidYes: 'told to go on', saidNo: 'told to stop' }
    : { yes: 'Allow once', no: 'Refuse', saidYes: 'allowed once', saidNo: 'refused' }
}
