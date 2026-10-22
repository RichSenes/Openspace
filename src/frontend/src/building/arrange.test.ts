import { describe, expect, it } from 'vitest'
import type { OfficeView, RoomView } from '../api/types'
import { FURNITURE } from '../art/fanPack'
import {
  addPiece,
  arrangementOf,
  boundsOf,
  furnitureLabel,
  handlesOf,
  layoutRequest,
  place,
  removePiece,
  resize,
  targetAt,
  withDrafts,
  type Arrangement,
} from './arrange'
import { layoutOffice } from './layout'

const room = (desks: number, extra: Partial<RoomView> = {}): RoomView => ({
  id: 'web',
  name: 'Web',
  context: '',
  procedure: [],
  desks: Array.from({ length: desks }, (_, i) => ({ id: `desk-${i}`, name: `Desk ${i}`, agentName: 'agent', model: null, roleId: null })),
  ...extra,
})

const office = (...rooms: RoomView[]): OfficeView => ({ id: 'allsafe', name: 'Allsafe', repositoryPath: null, theme: 'cozy', rooms })

const empty = (width = 10, depth = 4): Arrangement => ({ width, depth, desks: {}, furniture: [] })

describe('arrangementOf', () => {
  it('starts from the room as drawn, in pixels from its corner', () => {
    const box = layoutOffice(office(room(2)), 100).rooms[0]
    const arrangement = arrangementOf(box)
    expect(arrangement.width).toBe(box.width)
    expect(arrangement.depth).toBe(box.height - 2)
    expect(arrangement.desks['desk-0']).toEqual({ x: box.desks[0].x - box.x * 16, y: box.desks[0].y - box.y * 16 })
    expect(arrangement.furniture.map((p) => p.name)).toEqual(['plant'])
  })

  it('goes back to the same drawing once saved', () => {
    const before = layoutOffice(office(room(3)), 100).rooms[0]
    const after = layoutOffice(withDrafts(office(room(3)), { web: arrangementOf(before) }), 100).rooms[0]
    expect(after).toEqual(before)
  })
})

describe('place', () => {
  it('keeps desks inside the room with their heads in front of the wall at most', () => {
    const desk = { kind: 'desk', id: 'd' } as const
    const arrangement = { ...empty(), desks: { d: { x: 0, y: 40 } } }
    expect(place(arrangement, desk, -50, -50).desks.d).toEqual({ x: 0, y: 20 })
    expect(place(arrangement, desk, 999, 999).desks.d).toEqual({ x: 160 - 32, y: 96 - 46 })
    expect(place(arrangement, desk, 40.4, 30.6).desks.d).toEqual({ x: 40, y: 31 })
  })

  it('keeps standing furniture on the floor, rugs flat on it and posters on the wall', () => {
    const arrangement = addPiece(addPiece(addPiece(empty(), 'plant'), 'rug'), 'corpPoster')
    const plant = FURNITURE.plant.sprite
    expect(place(arrangement, { kind: 'piece', index: 0 }, 0, 0).furniture[0].y).toBe(34 - plant.height)
    expect(place(arrangement, { kind: 'piece', index: 1 }, 0, 0).furniture[1].y).toBe(32)
    expect(place(arrangement, { kind: 'piece', index: 2 }, 0, 90).furniture[2].y).toBe(Math.max(0, 32 - FURNITURE.corpPoster.sprite.height))
  })
})

describe('furniture', () => {
  it('is added in the middle of the floor and can be taken away', () => {
    const added = addPiece(empty(), 'plant')
    const box = boundsOf(added, { kind: 'piece', index: 0 })!
    expect(box.x + box.width / 2).toBeCloseTo(80, -1)
    expect(box.y).toBeGreaterThanOrEqual(32)
    expect(removePiece(added, 0).furniture).toEqual([])
  })

  it('is added where it covers nothing, when there is room', () => {
    const crowded = { ...empty(), desks: { a: { x: 64, y: 20 } } }
    const added = addPiece(crowded, 'plant')
    const plant = boundsOf(added, { kind: 'piece', index: 0 })!
    const desk = boundsOf(added, { kind: 'desk', id: 'a' })!
    expect(plant.x + plant.width <= desk.x || plant.x >= desk.x + desk.width || plant.y >= desk.y + desk.height).toBe(true)
  })

  it('reads its names as words', () => {
    expect(furnitureLabel('sideTable')).toBe('Side table')
    expect(furnitureLabel('plant')).toBe('Plant')
  })
})

describe('targetAt', () => {
  it('picks the thing in front and ignores the empty pixels of a sprite', () => {
    const arrangement = { ...addPiece(empty(), 'plant'), desks: { d: { x: 8, y: 30 } } }
    const plant = arrangement.furniture[0]
    expect(targetAt(arrangement, { x: 20, y: 50 })).toEqual({ kind: 'desk', id: 'd' })
    expect(targetAt(arrangement, { x: 20, y: 31 })).toBeUndefined()
    const row = FURNITURE.plant.sprite.frames[0].findIndex((r) => r.includes('.') && /[^.]/.test(r))
    const line = FURNITURE.plant.sprite.frames[0][row]
    expect(targetAt(arrangement, { x: plant.x + line.search(/[^.]/), y: plant.y + row })).toEqual({ kind: 'piece', index: 0 })
    expect(targetAt(arrangement, { x: plant.x + line.indexOf('.'), y: plant.y + row })).toBeUndefined()
  })

  it('prefers what stands in front of a rug', () => {
    const arrangement = place(place(addPiece(addPiece(empty(), 'rug'), 'plant'), { kind: 'piece', index: 0 }, 40, 50), { kind: 'piece', index: 1 }, 50, 40)
    const plant = boundsOf(arrangement, { kind: 'piece', index: 1 })!
    const row = FURNITURE.plant.sprite.frames[0].length - 2 // the last row of the pot
    const x = FURNITURE.plant.sprite.frames[0][row].search(/[^.]/)
    expect(targetAt(arrangement, { x: plant.x + x, y: plant.y + row })).toEqual({ kind: 'piece', index: 1 })
  })
})

describe('resize', () => {
  it('stays within the limits and brings things back inside', () => {
    const arrangement = { ...empty(20, 8), desks: { d: { x: 280, y: 100 } } }
    const small = resize(arrangement, 2, 1)
    expect([small.width, small.depth]).toEqual([4, 3])
    expect(small.desks.d).toEqual({ x: 64 - 32, y: 80 - 46 })
  })
})

describe('withDrafts', () => {
  it('shows a room as it will be saved, or back to automatic', () => {
    const arranged = room(1, { width: 12, depth: 5, furniture: [{ name: 'rug', x: 20, y: 40 }], desks: [{ id: 'd', name: 'D', agentName: 'a', model: null, roleId: null, spot: { x: 50, y: 30 } }] })
    const auto = withDrafts(office(arranged), { web: 'auto' }).rooms[0]
    expect([auto.width, auto.depth, auto.desks[0].spot, auto.furniture]).toEqual([null, null, null, []])
    const moved = withDrafts(office(arranged), { web: { width: 9, depth: 4, desks: {}, furniture: [] } }).rooms[0]
    expect([moved.width, moved.desks[0].spot]).toEqual([9, null])
  })

  it('becomes the request the API takes', () => {
    expect(layoutRequest({ width: 9, depth: 4, desks: { d: { x: 1, y: 2 } }, furniture: [{ name: 'rug', x: 3, y: 40 }] })).toEqual({
      width: 9,
      depth: 4,
      desks: [{ deskId: 'd', x: 1, y: 2 }],
      furniture: [{ name: 'rug', x: 3, y: 40 }],
    })
  })
})

describe('handlesOf', () => {
  it('puts a handle on the right wall, the front edge and the corner between them', () => {
    const box = { id: 'r', name: 'R', x: 2, y: 2, width: 10, height: 6, desks: [], props: [] }
    expect(handlesOf(box)).toEqual([
      { edge: 'right', x: 194, y: 80 },
      { edge: 'bottom', x: 112, y: 130 },
      { edge: 'corner', x: 194, y: 130 },
    ])
  })
})
