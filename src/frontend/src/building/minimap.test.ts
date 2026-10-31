import { describe, expect, it } from 'vitest'
import { STATION } from '../art/station'
import { TILE } from '../art/roomScene'
import type { BuildingLayout, RoomBox } from './layout'
import { centredOn, MAP_TILE, mapSize, mapView, minimapPixels, type RoomInk } from './minimap'

const room = (id: string, x: number, y: number): RoomBox => ({ id, name: id, x, y, width: 6, height: 5, desks: [], props: [], door: { wall: 'Front', offset: 2 }, windows: [] })

const layout: BuildingLayout = {
  rooms: [room('a', 1, 1), room('b', 8, 1)],
  floor: { x: 0, y: 0, width: 16, height: 10 },
  cuts: [{ x: 14, y: 8, width: 2, height: 2 }],
  doors: [],
  hall: [],
  planned: true,
  width: 18,
  height: 12,
}

const hall: RoomInk = { floor: '#111111', wall: '#222222', outline: '#000000' }
const inks = new Map<string, RoomInk>([['b', { floor: '#bbbbbb', wall: '#cccccc', outline: '#010101' }]])
const at = (pixels: { x: number; y: number; color: string }[], x: number, y: number) => pixels.find((p) => p.x === x && p.y === y)?.color

describe('the minimap', () => {
  it('is a few pixels per tile, enlarged by whole steps to fit its box', () => {
    expect(mapSize(layout)).toEqual({ width: 36, height: 24, zoom: 4 })
    expect(mapSize({ width: 80, height: 40 }).zoom).toBe(1)
  })

  it('draws the common floor, each room in its own colours and nothing where the building is cut', () => {
    const pixels = minimapPixels(layout, hall, inks, [], 0)
    expect(at(pixels, 0, 0)).toBe('#000000')
    expect(at(pixels, 1, 1)).toBe('#111111')
    expect(at(pixels, 9 * MAP_TILE + 1, 4 * MAP_TILE)).toBe('#bbbbbb')
    expect(at(pixels, 9 * MAP_TILE + 1, 1 * MAP_TILE + 1)).toBe('#cccccc')
    expect(at(pixels, 8 * MAP_TILE, 3 * MAP_TILE)).toBe('#010101')
    expect(at(pixels, 3 * MAP_TILE, 4 * MAP_TILE)).toBe('#111111')
    expect(at(pixels, 15 * MAP_TILE, 9 * MAP_TILE)).toBeUndefined()
  })

  it('marks agents by shape, blinking when they need you', () => {
    const agent = (state: 'working' | 'waiting' | 'failed') => ({ x: 3 * TILE - STATION.width / 2, y: 3 * TILE - STATION.height / 2, state })
    const centre = (state: 'working' | 'waiting' | 'failed', tick = 0) => at(minimapPixels(layout, hall, inks, [agent(state)], tick), 6, 6)
    expect(centre('working')).toBe('#7ee08a')
    expect(centre('failed')).toBe('#ff6b6b')
    expect(at(minimapPixels(layout, hall, inks, [agent('waiting')], 0), 6, 5)).toBe('#f2b84b')
    expect(at(minimapPixels(layout, hall, inks, [agent('waiting')], 2), 6, 5)).not.toBe('#f2b84b')
  })

  it('shows what the screen sees and centres the screen where it is clicked', () => {
    const camera = { x: -160, y: -80, scale: 2 }
    const screen = { width: 640, height: 320 }
    expect(mapView(camera, screen)).toEqual({ x: 10, y: 5, width: 40, height: 20 })
    const moved = centredOn(camera, screen, 30, 15)
    expect(mapView(moved, screen)).toEqual({ x: 10, y: 5, width: 40, height: 20 })
    expect(centredOn(camera, screen, 40, 15).x).toBeLessThan(camera.x)
  })
})
