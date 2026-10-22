import { describe, expect, it } from 'vitest'
import type { OfficeView, RoomView } from '../api/types'
import { layoutOffice } from './layout'
import { wallPlan, type Strip } from './walls'

const room = (id: string, extra: Partial<RoomView>): RoomView => ({ id, name: id, context: '', procedure: [], desks: [], width: 8, depth: 4, ...extra })

const planned = (...rooms: RoomView[]): OfficeView => ({ id: 'o', name: 'Allsafe', repositoryPath: null, theme: 'cozy', rooms, floorWidth: 20, floorDepth: 12 })

/** Whether any cap covers the point. */
const walled = (caps: Strip[], x: number, y: number) => caps.some((s) => x >= s.x && x < s.x + s.width && y >= s.y && y < s.y + s.height)

describe('wallPlan', () => {
  it('leaves the doorway open and frames it with two jambs', () => {
    const layout = layoutOffice(planned(room('web', { left: 0, top: 0, door: { wall: 'Front', offset: 3 } })))
    const plan = wallPlan(layout)
    // The front wall of the room is at y = (1 + 6) * 16; the door runs from x = (1 + 3) * 16 for two tiles.
    expect(walled(plan.caps, 64 + 16, 112)).toBe(false)
    expect(walled(plan.caps, 64 - 8, 112)).toBe(true)
    expect(walled(plan.caps, 96 + 8, 112)).toBe(true)
    expect(plan.jambs).toHaveLength(2)
  })

  it('opens a shared wall for a side door, whichever room draws it', () => {
    const layout = layoutOffice(planned(room('web', { left: 0, top: 0, door: { wall: 'Right', offset: 1 } }), room('identity', { left: 8, top: 0 })))
    const plan = wallPlan(layout)
    // The shared wall is at x = (1 + 8) * 16; the door opens from y = (1 + 2 + 1) * 16.
    expect(walled(plan.caps, 144, 64 + 16)).toBe(false)
    expect(walled(plan.caps, 144, 40)).toBe(true)
  })

  it('opens the building for a door in an outer wall', () => {
    const layout = layoutOffice(planned(room('web', { left: 0, top: 0, door: { wall: 'Left', offset: 0 } })))
    expect(walled(wallPlan(layout).caps, 16, 48 + 8)).toBe(false)
  })

  it('shows the face of a front wall towards the corridor, not of the outer one', () => {
    const plan = wallPlan(layoutOffice(planned(room('web', { left: 0, top: 0 }))))
    expect(plan.faces.length).toBeGreaterThan(0)
    expect(plan.faces.every((f) => f.y < (1 + 12) * 16)).toBe(true)
  })
})
