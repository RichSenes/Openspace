import type { WorldView } from '../../../api/types'
import type { OfficeSpec } from './layout'

/** Office colours, in order: the same office keeps its colour as long as the order holds. */
export const ACCENTS = ['#5b9bff', '#f2a948', '#3fd29a', '#ff7aa8', '#c48bff', '#e6d25a', '#5fd7e6', '#ff8a5c']

/**
 * The lab's setup from the real world: an area per office, with a seat for each desk and each
 * routine (routines live in the office they serve). An office of routines only reads as data
 * work; the rest as code, until offices say what kind of work they do.
 */
export function specsOf(world: WorldView): OfficeSpec[] {
  return world.offices.map((office, i) => {
    const desks = office.characters.filter((c) => c.kind === 'Desk').length
    const routines = office.characters.filter((c) => c.kind === 'Routine').length
    return {
      name: office.name,
      accent: ACCENTS[i % ACCENTS.length],
      agents: Math.max(1, desks + routines),
      flavor: desks === 0 && routines > 0 ? 'data' : 'code',
    }
  })
}
