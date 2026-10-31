import type { DeskInterns, Intern } from '../api/types'

/** How long the puff of smoke lasts when an intern arrives or leaves. */
export const PUFF_MS = 800
const PUFF_FRAMES = 4

/** Interns drawn beside a desk; more are listed on the agent's card. */
export const SPOTS = 2

/** Replaces the desk's previous interns with the new list. */
export function mergeInterns(list: DeskInterns[] | undefined, update: DeskInterns): DeskInterns[] {
  return [...(list ?? []).filter((d) => d.deskId !== update.deskId), update]
}

/** The interns per desk of one office. */
export function internsByDesk(list: DeskInterns[] | undefined, officeId: string): Map<string, Intern[]> {
  return new Map((list ?? []).filter((d) => d.officeId === officeId).map((d) => [d.deskId, d.interns]))
}

interface Staged {
  intern: Intern
  spot: number
  /** When it came in; minus infinity for the ones already there when the page opened. */
  arrivedAt: number
  leftAt?: number
}

/** An intern as drawn now: where it stands, whether it shows, and the frame of its puff if one is going on. */
export interface OnStage {
  intern: Intern
  spot: number
  visible: boolean
  puff?: number
}

/**
 * Who stands beside each desk, remembering arrivals and departures so they happen in a
 * puff of smoke: the intern shows from the middle of the puff when it arrives, and is
 * gone from the middle of it when it leaves. Interns already there when the page opens
 * simply stand there. Without animation they come and go at once.
 */
export class InternStage {
  private readonly desks = new Map<string, Staged[]>()
  private started = false

  update(current: ReadonlyMap<string, readonly Intern[]>, now: number, animate: boolean): void {
    for (const deskId of new Set([...this.desks.keys(), ...current.keys()])) {
      const ids = new Set((current.get(deskId) ?? []).map((i) => i.id))
      const staged = [...(this.desks.get(deskId) ?? [])]
      for (const s of staged) if (!ids.has(s.intern.id) && s.leftAt === undefined) s.leftAt = now
      for (const intern of current.get(deskId) ?? []) {
        const known = staged.find((s) => s.intern.id === intern.id)
        if (known) {
          known.intern = intern
          continue
        }
        const taken = new Set(staged.map((s) => s.spot))
        let spot = 0
        while (taken.has(spot)) spot++
        staged.push({ intern, spot, arrivedAt: this.started && animate ? now : -Infinity })
      }
      const kept = animate ? staged : staged.filter((s) => ids.has(s.intern.id))
      if (kept.length > 0) this.desks.set(deskId, kept)
      else this.desks.delete(deskId)
    }
    this.started = true
  }

  at(deskId: string, now: number): OnStage[] {
    const staged = this.desks.get(deskId) ?? []
    const left = staged.filter((s) => s.leftAt !== undefined && now - s.leftAt >= PUFF_MS)
    if (left.length > 0) this.desks.set(deskId, staged.filter((s) => !left.includes(s)))
    return staged
      .filter((s) => !left.includes(s) && s.spot < SPOTS)
      .map((s) => {
        const frame = (since: number) => Math.min(PUFF_FRAMES - 1, Math.floor((since / PUFF_MS) * PUFF_FRAMES))
        if (s.leftAt !== undefined) {
          const puff = frame(now - s.leftAt)
          return { intern: s.intern, spot: s.spot, visible: puff < PUFF_FRAMES / 2, puff }
        }
        if (now - s.arrivedAt < PUFF_MS) {
          const puff = frame(now - s.arrivedAt)
          return { intern: s.intern, spot: s.spot, visible: puff >= PUFF_FRAMES / 2, puff }
        }
        return { intern: s.intern, spot: s.spot, visible: true }
      })
  }
}
