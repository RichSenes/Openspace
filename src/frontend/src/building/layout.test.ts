import { describe, expect, it } from 'vitest'
import type { OfficeView, RoomView } from '../api/types'
import { bestLayout, fitScale, layoutOffice } from './layout'

const room = (name: string, desks: number): RoomView => ({
  id: name,
  name,
  context: '',
  procedure: [],
  desks: Array.from({ length: desks }, (_, i) => ({ id: `${name}-${i}`, name: `Desk ${i}`, agentName: 'agent', model: null, roleId: null })),
})

const office = (...rooms: RoomView[]): OfficeView => ({ id: 'e-corp', name: 'E Corp', repositoryPath: null, theme: 'cozy', rooms })

describe('layoutOffice', () => {
  it('is empty for an office without rooms', () => {
    expect(layoutOffice(office(), 100)).toEqual({ rooms: [], width: 0, height: 0 })
  })

  it('places rooms side by side without overlapping', () => {
    const { rooms } = layoutOffice(office(room('Web', 1), room('Identity', 0)), 100)
    expect(rooms[1].x).toBeGreaterThanOrEqual(rooms[0].x + rooms[0].width)
    expect(rooms[1].y).toBe(rooms[0].y)
  })

  it('wraps to a new row when a room does not fit', () => {
    const { rooms } = layoutOffice(office(room('Web', 3), room('Identity', 3)), 20)
    expect(rooms[1].x).toBe(rooms[0].x)
    expect(rooms[1].y).toBeGreaterThan(rooms[0].y + rooms[0].height - 1)
  })

  it('grows a room with its desks and keeps every station inside it', () => {
    const small = layoutOffice(office(room('A', 1)), 100).rooms[0]
    const big = layoutOffice(office(room('B', 9)), 100).rooms[0]
    expect(big.width).toBeGreaterThan(small.width)
    expect(big.height).toBeGreaterThan(small.height)
    for (const desk of big.desks) {
      expect(desk.x).toBeGreaterThanOrEqual(big.x * 16)
      expect(desk.x + 32).toBeLessThanOrEqual((big.x + big.width) * 16)
      expect(desk.y + 46).toBeLessThanOrEqual((big.y + big.height) * 16)
    }
  })

  it('puts desks in rows of four, one behind the other', () => {
    const desks = layoutOffice(office(room('Web', 5)), 100).rooms[0].desks
    expect(new Set(desks.slice(0, 4).map((d) => d.y)).size).toBe(1)
    expect(desks[4].y).toBeGreaterThan(desks[0].y)
    expect(desks[4].x).toBe(desks[0].x)
  })

  it('wraps rows of rooms below the tallest room of the row', () => {
    const { rooms } = layoutOffice(office(room('Big', 8), room('Small', 1), room('Next', 1)), 30)
    const next = rooms.find((r) => r.name === 'Next')!
    expect(next.y).toBeGreaterThanOrEqual(rooms[0].y + rooms[0].height)
  })
})

describe('fitScale', () => {
  it('uses whole-number zoom when there is room, up to 4x', () => {
    expect(fitScale({ width: 100, height: 100 }, { width: 350, height: 900 })).toBe(3)
    expect(fitScale({ width: 10, height: 10 }, { width: 1000, height: 1000 })).toBe(4)
  })

  it('shrinks below 1 only when the content is too big', () => {
    expect(fitScale({ width: 200, height: 100 }, { width: 100, height: 100 })).toBe(0.5)
  })
})

describe('bestLayout', () => {
  const three = office(room('Web', 2), room('Identity', 1), room('Compliance', 1))

  it('keeps rooms on one row in a wide viewport', () => {
    const { layout } = bestLayout(three, { width: 1600, height: 500 })
    expect(new Set(layout.rooms.map((r) => r.y)).size).toBe(1)
  })

  it('stacks rooms in a tall narrow viewport', () => {
    const { layout } = bestLayout(three, { width: 300, height: 1600 })
    expect(new Set(layout.rooms.map((r) => r.y)).size).toBe(3)
  })

  it('never picks a smaller zoom than the single row', () => {
    const viewport = { width: 1100, height: 700 }
    const single = layoutOffice(three, Number.MAX_SAFE_INTEGER)
    const { scale } = bestLayout(three, viewport)
    expect(scale).toBeGreaterThanOrEqual(fitScale({ width: single.width * 16, height: single.height * 16 }, viewport))
  })
})
