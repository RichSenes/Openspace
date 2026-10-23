import { describe, expect, it } from 'vitest'
import type { OfficeView, RoomView } from '../api/types'
import { FURNITURE } from '../art/fanPack'
import {
  addCommon,
  addCut,
  addPiece,
  addWindow,
  arrangementOf,
  boundsOf,
  commonAt,
  currentFloor,
  cutFits,
  doorAt,
  editFloor,
  editRoom,
  fits,
  floorArrangementOf,
  furnitureLabel,
  handlesOf,
  moveRoom,
  NO_DRAFTS,
  place,
  planRequest,
  removeCut,
  removePiece,
  removeWindow,
  resize,
  resizeFloor,
  setCut,
  stretchCut,
  targetAt,
  tidyDesks,
  withDrafts,
  type Arrangement,
  type FloorArrangement,
} from './arrange'
import { layoutOffice } from './layout'

const room = (id: string, desks: number, extra: Partial<RoomView> = {}): RoomView => ({
  id,
  name: id,
  context: '',
  procedure: [],
  desks: Array.from({ length: desks }, (_, i) => ({ id: `${id}-${i}`, name: `Desk ${i}`, agentName: 'agent', model: null, roleId: null })),
  ...extra,
})

const office = (...rooms: RoomView[]): OfficeView => ({ id: 'allsafe', name: 'Allsafe', repositoryPath: null, theme: 'cozy', rooms })

const empty = (width = 10, depth = 4): Arrangement => ({ width, depth, desks: {}, furniture: [], door: { wall: 'Front', offset: 4 }, windows: [] })

describe('arrangementOf', () => {
  it('starts from the room as drawn, in pixels from its corner', () => {
    const box = layoutOffice(office(room('web', 2))).rooms[0]
    const arrangement = arrangementOf(box)
    expect(arrangement.width).toBe(box.width)
    expect(arrangement.depth).toBe(box.height - 2)
    expect(arrangement.desks['web-0']).toEqual({ x: box.desks[0].x - box.x * 16, y: box.desks[0].y - box.y * 16 })
    expect(arrangement.furniture.map((p) => p.name)).toEqual(['plant'])
    expect(arrangement.door).toEqual(box.door)
  })

  it('goes back to the same drawing once saved', () => {
    const before = layoutOffice(office(room('web', 3)))
    const drafts = { rooms: { web: arrangementOf(before.rooms[0]) }, floor: floorArrangementOf(before) }
    const after = layoutOffice(withDrafts(office(room('web', 3)), drafts))
    expect(after.rooms[0]).toEqual(before.rooms[0])
    expect(after.planned).toBe(true)
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

  it('slides windows along the back wall only', () => {
    const arrangement = { ...empty(), windows: [10] }
    expect(place(arrangement, { kind: 'window', index: 0 }, 300, 70).windows).toEqual([160 - 24])
    expect(place(arrangement, { kind: 'window', index: 0 }, -5, 0).windows).toEqual([0])
  })
})

describe('doorAt', () => {
  it('takes the wall nearest the point and centres the door on it', () => {
    const arrangement = empty(10, 4) // 160 by 96 pixels, back wall 32 tall
    expect(doorAt(arrangement, { x: 80, y: 94 })).toEqual({ wall: 'Front', offset: 4 })
    expect(doorAt(arrangement, { x: 20, y: 14 })).toEqual({ wall: 'Back', offset: 0 })
    expect(doorAt(arrangement, { x: 2, y: 64 })).toEqual({ wall: 'Left', offset: 1 })
    expect(doorAt(arrangement, { x: 158, y: 90 })).toEqual({ wall: 'Right', offset: 2 })
  })

  it('is what dragging the door does', () => {
    expect(place(empty(), { kind: 'door' }, 158, 50).door.wall).toBe('Right')
  })
})

describe('furniture', () => {
  it('is added where it covers nothing, when there is room', () => {
    const crowded = { ...empty(), desks: { a: { x: 64, y: 20 } } }
    const added = addPiece(crowded, 'plant')
    const plant = boundsOf(added, { kind: 'piece', index: 0 })!
    const desk = boundsOf(added, { kind: 'desk', id: 'a' })!
    expect(plant.x + plant.width <= desk.x || plant.x >= desk.x + desk.width || plant.y >= desk.y + desk.height).toBe(true)
    expect(removePiece(added, 0).furniture).toEqual([])
  })

  it('reads its names as words', () => {
    expect(furnitureLabel('sideTable')).toBe('Side table')
    expect(furnitureLabel('plant')).toBe('Plant')
  })
})

describe('windows', () => {
  it('are added near the middle of the back wall, beside each other and never on a back door', () => {
    const one = addWindow(empty())
    expect(one.windows).toEqual([68])
    const two = addWindow(one)
    expect(Math.abs(two.windows[1] - two.windows[0])).toBeGreaterThanOrEqual(28)
    const withDoor = addWindow({ ...empty(), door: { wall: 'Back', offset: 4 } })
    expect(withDoor.windows[0] + 24 <= 64 || withDoor.windows[0] >= 96).toBe(true)
    expect(removeWindow(two, 0).windows).toEqual([two.windows[1]])
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

  it('finds the door and the windows on their walls', () => {
    const arrangement = { ...empty(), windows: [100] }
    expect(targetAt(arrangement, { x: 110, y: 10 })).toEqual({ kind: 'window', index: 0 })
    expect(targetAt(arrangement, { x: 70, y: 93 })).toEqual({ kind: 'door' })
  })
})

describe('resize', () => {
  it('stays within the limits and brings things back inside, the door and the windows too', () => {
    const arrangement = { ...empty(20, 8), desks: { d: { x: 280, y: 100 } }, door: { wall: 'Front' as const, offset: 15 }, windows: [290] }
    const small = resize(arrangement, 2, 1)
    expect([small.width, small.depth]).toEqual([4, 3])
    expect(small.desks.d).toEqual({ x: 64 - 32, y: 80 - 46 })
    expect(small.door.offset).toBe(2)
    expect(small.windows).toEqual([64 - 24])
  })
})

describe('tidyDesks', () => {
  it('puts desks back in rows of four', () => {
    const tidy = tidyDesks({ ...empty(16, 8), desks: { a: { x: 200, y: 90 }, b: { x: 3, y: 70 } } })
    expect(tidy.desks.a.y).toBe(tidy.desks.b.y)
    expect(tidy.desks.b.x - tidy.desks.a.x).toBe(48)
  })
})

describe('the floor plan', () => {
  const floor = (): FloorArrangement => ({ width: 20, depth: 12, furniture: [], rooms: { a: { left: 0, top: 0 }, b: { left: 10, top: 0 } }, cuts: [] })
  const sizes = { a: { width: 8, height: 6 }, b: { width: 8, height: 6 } }

  it('lets a room move where it fits and keeps it where it would overlap or leave the building', () => {
    expect(moveRoom(floor(), sizes, 'a', 0, 6).rooms.a).toEqual({ left: 0, top: 6 })
    expect(moveRoom(floor(), sizes, 'a', 5, 0).rooms.a).toEqual({ left: 0, top: 0 })
    expect(moveRoom(floor(), sizes, 'a', 50, 50).rooms.a).toEqual({ left: 12, top: 6 })
    expect(fits(floor(), sizes, 'a', 2, 0, 8, 6)).toBe(true)
    expect(fits(floor(), sizes, 'a', 3, 0, 8, 6)).toBe(false)
  })

  it('never shrinks the building below its rooms', () => {
    const small = resizeFloor(floor(), sizes, 3, 3)
    expect([small.width, small.depth]).toEqual([18, 6])
  })

  it('furnishes the common floor outside the rooms', () => {
    const added = addCommon(floor(), sizes, 'vendingMachine')
    const piece = added.furniture[0]
    const box = { x: piece.x, y: piece.y, width: FURNITURE.vendingMachine.sprite.width, height: FURNITURE.vendingMachine.sprite.height }
    for (const [id, at] of Object.entries(floor().rooms)) {
      const r = { x: at.left * 16, y: at.top * 16, width: sizes[id as 'a'].width * 16, height: sizes[id as 'a'].height * 16 }
      expect(box.x >= r.x + r.width || box.x + box.width <= r.x || box.y >= r.y + r.height || box.y + box.height <= r.y).toBe(true)
    }
    expect(commonAt(added, { x: piece.x + 8, y: piece.y + 16 })).toBe(0)
  })
})

describe('the shape of the building', () => {
  // Two rooms along the back wall of a 20 by 12 building: the front is free.
  const floor = (): FloorArrangement => ({ width: 20, depth: 12, furniture: [], rooms: { a: { left: 0, top: 0 }, b: { left: 10, top: 0 } }, cuts: [] })
  const sizes = { a: { width: 8, height: 6 }, b: { width: 8, height: 6 } }

  it('cuts the first free corner, a third of the building each way', () => {
    const cut = addCut(floor(), sizes)
    // The back corners hold rooms: the front right one is cut.
    expect(cut.cuts).toEqual([{ left: 14, top: 8, width: 6, height: 4 }])
    expect(addCut(cut, sizes).cuts[1]).toEqual({ left: 0, top: 8, width: 6, height: 4 })
  })

  it('keeps cuts inside the building, out of rooms and apart from each other', () => {
    const one = { ...floor(), cuts: [{ left: 14, top: 8, width: 6, height: 4 }] }
    expect(cutFits(one, sizes, 0, { left: 14, top: 7, width: 6, height: 4 })).toBe(true)
    expect(cutFits(one, sizes, 0, { left: 14, top: 5, width: 6, height: 4 })).toBe(false)
    expect(cutFits(one, sizes, 0, { left: 16, top: 8, width: 6, height: 4 })).toBe(false)
    expect(cutFits(one, sizes, 1, { left: 12, top: 9, width: 4, height: 2 })).toBe(false)
    expect(setCut(one, sizes, 0, { left: 14, top: 5, width: 6, height: 4 })).toBe(one)
  })

  it('stretches a cut by any edge, never below a tile', () => {
    const cut = { left: 4, top: 4, width: 4, height: 4 }
    expect(stretchCut(cut, 'left', 2, 0)).toEqual({ left: 2, top: 4, width: 6, height: 4 })
    expect(stretchCut(cut, 'top', 0, 9)).toEqual({ left: 4, top: 7, width: 4, height: 1 })
    expect(stretchCut(cut, 'corner', 10, 5)).toEqual({ left: 4, top: 4, width: 6, height: 1 })
  })

  it('keeps rooms and furniture out of cuts', () => {
    const shaped = { ...floor(), cuts: [{ left: 0, top: 8, width: 10, height: 4 }] }
    expect(moveRoom(shaped, sizes, 'a', 0, 6).rooms.a).toEqual({ left: 0, top: 0 })
    const furnished = setCut({ ...floor(), furniture: [{ name: 'plant', x: 16, y: 150 }], cuts: [{ left: 15, top: 8, width: 5, height: 4 }] }, sizes, 0, { left: 0, top: 8, width: 5, height: 4 })
    // The plant stood where the cut went: it stepped out, to the nearest free spot.
    expect(furnished.furniture[0].y + FURNITURE.plant.sprite.height).toBeLessThanOrEqual(8 * 16)
  })

  it('keeps an edge cut at its edge when the building grows', () => {
    const grown = resizeFloor({ ...floor(), cuts: [{ left: 14, top: 8, width: 6, height: 4 }] }, sizes, 24, 14)
    expect(grown.cuts).toEqual([{ left: 14, top: 8, width: 10, height: 6 }])
    const shrunk = resizeFloor(grown, sizes, 15, 7)
    expect([shrunk.width, shrunk.depth]).toEqual([18, 9])
    expect(shrunk.cuts).toEqual([{ left: 14, top: 8, width: 4, height: 1 }])
  })

  it('goes with the plan to the API and away with the automatic building', () => {
    const allsafe = office(room('web', 1))
    const drafts = editFloor(allsafe, NO_DRAFTS, (f, s) => addCut(resizeFloor(f, s, 20, 12), s))
    const shown = withDrafts(allsafe, drafts)
    expect(planRequest(shown).cuts).toHaveLength(1)
    expect(layoutOffice(shown).cuts).toHaveLength(1)
    expect(removeCut(currentFloor(shown, drafts), 0).cuts).toEqual([])
    expect(withDrafts(shown, { ...NO_DRAFTS, auto: true }).cuts).toEqual([])
  })
})

describe('editing', () => {
  const allsafe = () => office(room('web', 2), room('identity', 1))

  it('fixes the building as drawn at the first change', () => {
    const drafts = editRoom(allsafe(), NO_DRAFTS, 'web', (a) => resize(a, a.width, a.depth + 1))
    expect(drafts.floor).toBeDefined()
    const shown = withDrafts(allsafe(), drafts)
    expect(layoutOffice(shown).planned).toBe(true)
  })

  it('leaves undone a change that would push a room into another', () => {
    const drafts = editRoom(allsafe(), NO_DRAFTS, 'web', (a) => resize(a, a.width + 5, a.depth))
    expect(drafts).toBe(NO_DRAFTS)
  })

  it('moves rooms and becomes the plan the API takes', () => {
    const drafts = editFloor(allsafe(), NO_DRAFTS, (f, sizes) => moveRoom(resizeFloor(f, sizes, f.width, f.depth + 10), sizes, 'identity', 0, 12))
    const shown = withDrafts(allsafe(), drafts)
    expect(currentFloor(shown, drafts).rooms.identity).toEqual({ left: 0, top: 12 })
    const plan = planRequest(shown)
    expect(plan.rooms.find((r) => r.roomId === 'identity')).toMatchObject({ left: 0, top: 12 })
    expect(plan.rooms[0].layout.desks).toHaveLength(2)
    expect(plan.rooms[0].layout.door.wall).toBe('Front')
  })

  it('shows the automatic office when asked to go back to it', () => {
    const planned = { ...office(room('web', 1, { width: 9, depth: 4, left: 3, top: 2, windows: [16] })), floorWidth: 30, floorDepth: 20 }
    const auto = withDrafts(planned, { ...NO_DRAFTS, auto: true })
    expect([auto.floorWidth, auto.rooms[0].left, auto.rooms[0].width, auto.rooms[0].windows]).toEqual([null, null, null, []])
  })
})

describe('handlesOf', () => {
  it('puts a handle on the right wall, the front edge and the corner between them', () => {
    expect(handlesOf({ x: 2, y: 2, width: 10, height: 6 })).toEqual([
      { edge: 'right', x: 194, y: 104 },
      { edge: 'bottom', x: 152, y: 130 },
      { edge: 'corner', x: 194, y: 130 },
    ])
  })

  it('adds the left and back edges of a cut', () => {
    expect(handlesOf({ x: 2, y: 2, width: 10, height: 6 }, true).slice(3)).toEqual([
      { edge: 'left', x: 30, y: 80 },
      { edge: 'top', x: 112, y: 30 },
    ])
  })
})
