import { describe, expect, it } from 'vitest'
import type { OfficeView, RoomView } from '../api/types'
import { doorOf, freeSpot, inBuilding, layoutOffice } from './layout'

const room = (name: string, desks: number, extra: Partial<RoomView> = {}): RoomView => ({
  id: name,
  name,
  context: '',
  procedure: [],
  desks: Array.from({ length: desks }, (_, i) => ({ id: `${name}-${i}`, name: `Desk ${i}`, agentName: 'agent', model: null, roleId: null })),
  ...extra,
})

const office = (...rooms: RoomView[]): OfficeView => ({ id: 'e-corp', name: 'E Corp', repositoryPath: null, theme: 'cozy', rooms })

describe('layoutOffice, automatic', () => {
  it('is empty for an office without rooms', () => {
    expect(layoutOffice(office())).toMatchObject({ rooms: [], doors: [], hall: [], width: 0, height: 0 })
  })

  it('places rooms side by side, wall to wall', () => {
    const { rooms } = layoutOffice(office(room('Web', 1), room('Identity', 0)))
    expect(rooms[1].x).toBe(rooms[0].x + rooms[0].width)
    expect(rooms[1].y).toBe(rooms[0].y)
  })

  it('never depends on the screen: about the square root of the rooms in a row', () => {
    const { rooms } = layoutOffice(office(room('A', 1), room('B', 1), room('C', 1), room('D', 1), room('E', 1)))
    const rows = new Set(rooms.map((r) => r.y))
    expect(rows.size).toBe(2)
    expect(rooms.filter((r) => r.y === rooms[0].y)).toHaveLength(3)
  })

  it('grows a room with its desks and keeps every station inside it', () => {
    const small = layoutOffice(office(room('A', 1))).rooms[0]
    const big = layoutOffice(office(room('B', 9))).rooms[0]
    expect(big.width).toBeGreaterThan(small.width)
    expect(big.height).toBeGreaterThan(small.height)
    for (const desk of big.desks) {
      expect(desk.x).toBeGreaterThanOrEqual(big.x * 16)
      expect(desk.x + 32).toBeLessThanOrEqual((big.x + big.width) * 16)
      expect(desk.y + 46).toBeLessThanOrEqual((big.y + big.height) * 16)
    }
  })

  it('leaves a corridor in front of each row, inside the building', () => {
    const { rooms, floor } = layoutOffice(office(room('Web', 3), room('Identity', 1), room('Compliance', 2)))
    const second = rooms.find((r) => r.y > rooms[0].y)!
    const firstRow = rooms.filter((r) => r.y === rooms[0].y)
    expect(second.y).toBe(Math.max(...firstRow.map((r) => r.y + r.height)) + 3)
    for (const r of rooms) {
      expect(r.x + r.width).toBeLessThanOrEqual(floor.x + floor.width)
      expect(r.y + r.height + 3).toBeLessThanOrEqual(floor.y + floor.height)
    }
  })

  it('opens a door in the middle of the front wall of every room, unless it was moved', () => {
    const { rooms, doors } = layoutOffice(office(room('Web', 3), room('Identity', 1, { width: 8, depth: 4, door: { wall: 'Left', offset: 1 } })))
    expect(doors[0]).toMatchObject({ wall: 'Front', y: (rooms[0].y + rooms[0].height) * 16 })
    expect(doors[0].x).toBeGreaterThan(rooms[0].x * 16)
    expect(doors[1]).toEqual({ roomId: 'Identity', wall: 'Left', x: rooms[1].x * 16, y: (rooms[1].y + 2 + 1) * 16, length: 32 })
  })

  it('puts corridor furniture of the theme in the corridor, never in front of a door', () => {
    const { doors, hall } = layoutOffice({ ...office(room('Web', 3), room('Identity', 1)), theme: 'neonDistrict' })
    expect(hall.map((p) => p.name)).toContain('vendingMachine')
    for (const p of hall) for (const d of doors) expect(p.x >= d.x + d.length || p.x + 18 <= d.x).toBe(true)
  })

  it('keeps the size, the desks, the furniture and the windows of an arranged room', () => {
    const desk = { id: 'a', name: 'a', agentName: 'agent', model: null, roleId: null, spot: { x: 70, y: 50 } }
    const arranged = room('Web', 0, { width: 12, depth: 6, desks: [desk], windows: [16, 80], furniture: [{ name: 'rug', x: 10, y: 40 }, { name: 'not-a-prop', x: 0, y: 40 }] })
    const box = layoutOffice(office(arranged)).rooms[0]
    expect([box.width, box.height]).toEqual([12, 8])
    expect(box.desks[0]).toMatchObject({ x: box.x * 16 + 70, y: box.y * 16 + 50 })
    expect(box.props).toEqual([{ name: 'rug', x: 10, y: 40 }])
    expect(box.windows).toEqual([16, 80])
  })
})

describe('layoutOffice, drawn by hand', () => {
  const planned = (...rooms: RoomView[]): OfficeView => ({ ...office(...rooms), floorWidth: 30, floorDepth: 16, furniture: [{ name: 'plant', x: 100, y: 200 }] })

  it('keeps the building size, every room where it was put and the common furniture', () => {
    const layout = layoutOffice(planned(room('Web', 1, { width: 8, depth: 4, left: 5, top: 3 })))
    expect(layout.planned).toBe(true)
    expect(layout.floor).toEqual({ x: 1, y: 1, width: 30, height: 16 })
    expect(layout.rooms[0]).toMatchObject({ x: 6, y: 4, width: 8, height: 6 })
    expect(layout.hall).toEqual([{ name: 'plant', x: 116, y: 216 }])
    expect([layout.width, layout.height]).toEqual([32, 18])
  })

  it('gives a room without a place the first free one', () => {
    const layout = layoutOffice(planned(room('Web', 1, { width: 8, depth: 4, left: 0, top: 0 }), room('New', 1)))
    const [web, added] = layout.rooms
    expect(added.y).toBe(web.y)
    expect(added.x).toBe(web.x + web.width)
  })

  it('takes cuts out of the building and finds new rooms a place outside them', () => {
    const layout = layoutOffice({ ...planned(room('New', 1)), cuts: [{ left: 0, top: 0, width: 12, height: 8 }] })
    expect(layout.cuts).toEqual([{ x: 1, y: 1, width: 12, height: 8 }])
    expect(inBuilding(layout, 5, 5)).toBe(false)
    expect(inBuilding(layout, 13, 5)).toBe(true)
    expect(inBuilding(layout, 40, 5)).toBe(false)
    expect(layout.rooms[0].x).toBe(13)
  })

  it('makes the building deeper when a new room fits nowhere', () => {
    const full = { ...planned(room('Web', 1, { width: 30, depth: 14, left: 0, top: 0 }), room('New', 1)) }
    const layout = layoutOffice(full)
    expect(layout.rooms[1].y).toBe(17)
    expect(layout.floor.height).toBeGreaterThan(16)
  })
})

describe('freeSpot', () => {
  it('finds the first place, back to front and left to right, where a box fits', () => {
    const floor = { x: 1, y: 1, width: 20, height: 10 }
    expect(freeSpot([], floor, 5, 5)).toEqual({ x: 1, y: 1 })
    expect(freeSpot([{ x: 1, y: 1, width: 10, height: 10 }], floor, 5, 5)).toEqual({ x: 11, y: 1 })
    expect(freeSpot([{ x: 1, y: 1, width: 20, height: 10 }], floor, 5, 5)).toBeUndefined()
  })
})

describe('doorOf', () => {
  it('puts a back door on the top wall and a side door below the back wall', () => {
    const box = { id: 'r', name: 'R', x: 2, y: 3, width: 8, height: 6, desks: [], props: [], windows: [] }
    expect(doorOf({ ...box, door: { wall: 'Back', offset: 2 } })).toMatchObject({ x: 64, y: 48 })
    expect(doorOf({ ...box, door: { wall: 'Right', offset: 0 } })).toMatchObject({ x: 160, y: 80 })
  })
})
