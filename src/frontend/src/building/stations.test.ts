import { describe, expect, it } from 'vitest'
import { STATION } from '../art/station'
import type { BuildingLayout, RoomBox } from './layout'
import { placesOf } from './stations'

const room: RoomBox = {
  id: 'r',
  name: 'Lab',
  x: 2,
  y: 2,
  width: 12,
  height: 8,
  desks: [{ id: 'd', name: 'Desk', x: 2 * 16 + 64, y: 2 * 16 + 40 }],
  props: [
    { name: 'bookshelf', x: 10, y: 34 },
    { name: 'coffeeMachine', x: 150, y: 80 },
    { name: 'plant', x: 120, y: 30 },
    { name: 'corpPoster', x: 60, y: 4 },
  ],
  door: { wall: 'Front', offset: 4 },
  windows: [],
}

const layout: BuildingLayout = { rooms: [room], floor: { x: 1, y: 1, width: 14, height: 10 }, cuts: [], doors: [], hall: [], planned: true, width: 16, height: 12 }

describe('placesOf', () => {
  it('finds the furniture with a purpose in the room, facing it for work and the room for a break', () => {
    const [place] = placesOf(layout)
    expect(place.stations.map((s) => [s.errand, s.facing])).toEqual([
      ['documents', 'back'],
      ['break', 'front'],
    ])
  })

  it('seats the agent where it sits at its desk, with a way out on each side', () => {
    const [place] = placesOf(layout)
    const desk = room.desks[0]
    expect(place.seat).toEqual({ x: desk.x + 8, y: desk.y + STATION.deskY - 14 })
    expect(place.exits[0].x).toBeLessThan(desk.x)
    expect(place.exits[1].x).toBeGreaterThan(desk.x + STATION.width - 24)
  })
})
