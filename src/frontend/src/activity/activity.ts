import type { DeskActivity } from '../api/types'

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
