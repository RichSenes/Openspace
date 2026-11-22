import { describe, expect, it } from 'vitest'
import type { OfficeView, RoomView } from '../api/types'
import type { AgentState } from '../art/agentStates'
import { layoutOffice } from './layout'
import { BRAIN_EVERY, eventOf, findRoute, LINGER, SHIORI_PACE, ShioriStage, shioriWorld, STALE, TALK, VISIT, type ShioriWorld } from './shiori'

const room = (name: string, desks: number, extra: Partial<RoomView> = {}): RoomView => ({
  id: name,
  name,
  context: '',
  procedure: [],
  desks: Array.from({ length: desks }, (_, i) => ({ id: `${name}-${i}`, name: `Desk ${i}`, agentName: 'agent', model: null, roleId: null })),
  ...extra,
})

const office = (...rooms: RoomView[]): OfficeView => ({ id: 'e-corp', name: 'E Corp', repositoryPath: null, theme: 'cozy', rooms })

const states = (entries: [string, AgentState][]) => new Map(entries)

/** Runs the stage from `from` to `to` in steps, as the canvas does five times a second. */
function run(stage: ShioriStage, world: ShioriWorld, s: ReadonlyMap<string, AgentState>, from: number, to: number, step = 200) {
  for (let t = from; t <= to; t += step) stage.update(world, s, t)
}

describe('eventOf', () => {
  it('tells a start, a failure, a question and a review, and nothing else', () => {
    expect(eventOf('idle', 'working')).toBe('started')
    expect(eventOf('queued', 'reading')).toBe('started')
    expect(eventOf('working', 'reading')).toBeNull()
    expect(eventOf('working', 'failed')).toBe('failed')
    expect(eventOf('working', 'waiting')).toBe('waiting')
    expect(eventOf('working', 'done')).toBe('review')
    expect(eventOf('done', 'idle')).toBeNull()
    expect(eventOf('failed', 'failed')).toBeNull()
  })
})

describe('shioriWorld and findRoute', () => {
  const layout = layoutOffice(office(room('Web', 2), room('Identity', 1)))
  const world = shioriWorld(layout)
  const regionAt = (x: number, y: number) => world.region[y * world.cols + x]
  const open = (x: number, y: number) => world.walkable[y * world.cols + x]

  it('closes back walls and desks, opens floors and the corridor', () => {
    const web = layout.rooms[0]
    expect(open(web.x + 1, web.y)).toBe(false)
    expect(open(web.x + 1, web.y + web.height - 1)).toBe(true)
    const desk = web.desks[0]
    expect(open(Math.floor((desk.x + 16) / 16), Math.floor((desk.y + 36) / 16))).toBe(false)
    expect(regionAt(web.x + 1, web.y + web.height)).toBe('hall')
  })

  it('goes from one room to the other through both doors and the corridor', () => {
    const [web, identity] = layout.rooms
    const from = { x: web.x + 1, y: web.y + web.height - 1 }
    const to = { x: identity.x + identity.width - 2, y: identity.y + identity.height - 1 }
    const route = findRoute(world, from, to)!
    expect(route[0]).toEqual(from)
    expect(route[route.length - 1]).toEqual(to)
    // Every leg is straight, and some of the way runs below the rooms, in the corridor.
    route.slice(1).forEach((t, i) => expect(t.x === route[i].x || t.y === route[i].y).toBe(true))
    expect(route.some((t) => regionAt(t.x, t.y) === 'hall')).toBe(true)
  })

  it('never walks through the wall two rooms share', () => {
    const [web, identity] = layout.rooms
    const route = findRoute(world, { x: web.x + web.width - 1, y: web.y + web.height - 1 }, { x: identity.x, y: identity.y + identity.height - 1 })!
    expect(route.length).toBeGreaterThan(2)
  })

  it('finds no way out of a room whose only door opens on nothing', () => {
    const shut = layoutOffice({ ...office(room('A', 1)), floorWidth: 14, floorDepth: 12 })
    shut.rooms[0] = { ...shut.rooms[0], x: 1, y: 1, door: { wall: 'Back', offset: 1 } }
    const r = shut.rooms[0]
    const w = shioriWorld(shut)
    expect(findRoute(w, { x: 2, y: r.y + r.height - 1 }, { x: 13, y: 12 })).toBeNull()
  })

  it('has a place beside every desk, one for the brain and some to wander to', () => {
    for (const d of layout.rooms.flatMap((r) => r.desks)) expect(world.desks.get(d.id)!.length).toBeGreaterThan(0)
    expect(world.brain.length).toBeGreaterThan(0)
    expect(world.wander.length).toBeGreaterThanOrEqual(3)
  })

  it('never stands about over a desk or its name', () => {
    const big = layoutOffice(office(room('Web', 6), room('Identity', 3)))
    const desks = big.rooms.flatMap((r) => r.desks)
    for (const s of shioriWorld(big).wander)
      for (const d of desks) expect(s.x + 24 <= d.x - 2 || s.x >= d.x + 34 || s.y + 32 <= d.y + 8 || s.y >= d.y + 54, `${s.x},${s.y}`).toBe(true)
  })

  it('takes the shelves and boards as the brain when there are some', () => {
    const shelves = layoutOffice(office(room('Lab', 1, { width: 10, depth: 6, furniture: [{ name: 'bookshelf', x: 100, y: 10 }] })))
    const w = shioriWorld(shelves)
    expect(w.brain).toHaveLength(1)
    expect(w.brain[0].facing).toBe('back')
    expect(w.region[w.brain[0].tile.y * w.cols + w.brain[0].tile.x]).toBe('Lab')
  })
})

describe('ShioriStage', () => {
  const layout = layoutOffice(office(room('Web', 2), room('Identity', 1)))
  const world = shioriWorld(layout)
  const quiet = states([['Web-0', 'idle'], ['Web-1', 'idle'], ['Identity-0', 'idle']])

  it('takes what desks show on arrival as old news', () => {
    const stage = new ShioriStage()
    stage.update(world, states([['Web-0', 'failed']]), 0)
    expect(stage.pending().size).toBe(0)
    expect(stage.at(0)).toMatchObject({ mode: 'idle' })
  })

  it('walks to a desk whose task changed, talks there about it, then leaves', () => {
    const stage = new ShioriStage()
    stage.update(world, quiet, 0)
    const failed = states([['Web-0', 'idle'], ['Web-1', 'idle'], ['Identity-0', 'failed']])
    stage.update(world, failed, 1000)
    expect(stage.at(1000)).toMatchObject({ mode: 'walking', heading: 'Identity-0' })
    let t = 1000
    while (stage.at(t)?.mode === 'walking' && t < 120_000) stage.update(world, failed, (t += 200))
    const there = stage.at(t)!
    expect(there).toMatchObject({ mode: 'talking', talk: { deskId: 'Identity-0', event: 'failed' } })
    expect(world.desks.get('Identity-0')!.some((s) => s.x === there.x && s.y === there.y && s.facing === there.facing)).toBe(true)
    run(stage, world, failed, t, t + TALK - 200)
    expect(stage.at(t + TALK - 200)?.mode).toBe('talking')
    stage.update(world, failed, t + TALK)
    expect(stage.at(t + TALK)?.mode).toBe('walking')
    expect(stage.at(t + TALK)?.heading).toBeUndefined()
  })

  it('goes to the most urgent news first: someone waiting before a start', () => {
    const stage = new ShioriStage()
    stage.update(world, quiet, 0)
    stage.update(world, states([['Web-0', 'working'], ['Web-1', 'waiting'], ['Identity-0', 'idle']]), 1000)
    expect(stage.at(1000)?.heading).toBe('Web-1')
  })

  it('leaves a stroll for news', () => {
    const stage = new ShioriStage()
    stage.update(world, quiet, 0)
    stage.update(world, quiet, LINGER)
    expect(stage.at(LINGER)).toMatchObject({ mode: 'walking', heading: undefined })
    stage.update(world, states([['Web-0', 'done'], ['Web-1', 'idle'], ['Identity-0', 'idle']]), LINGER + 600)
    expect(stage.at(LINGER + 600)?.heading).toBe('Web-0')
  })

  it('forgets news nobody went to in time, and news of desks that went away', () => {
    const started = states([['Web-0', 'working'], ['Web-1', 'idle'], ['Identity-0', 'idle']])
    const stage = new ShioriStage()
    stage.update(world, quiet, 0)
    stage.update(world, started, 1)
    expect(stage.pending().has('Web-0')).toBe(true)
    stage.update(world, states([['Web-1', 'idle']]), 2)
    expect(stage.pending().size).toBe(0)
    const again = new ShioriStage()
    again.update(world, quiet, 0)
    again.update(world, started, 1)
    again.update(world, started, 1 + STALE + 1)
    expect(again.pending().size).toBe(0)
  })

  it('visits the brain every few outings and stays a while', () => {
    const stage = new ShioriStage()
    let t = 0
    stage.update(world, quiet, t)
    const seen: string[] = ['idle']
    while (t < 600_000 && !seen.includes('visiting')) {
      t += 200
      stage.update(world, quiet, t)
      const mode = stage.at(t)!.mode
      if (mode !== seen[seen.length - 1]) seen.push(mode)
    }
    // The stand she starts at, then a stroll and a rest before every brain visit.
    expect(seen.filter((m) => m === 'idle').length).toBe(BRAIN_EVERY)
    const there = stage.at(t)!
    expect(world.brain.some((s) => s.x === there.x && s.y === there.y)).toBe(true)
    run(stage, world, quiet, t, t + VISIT - 200)
    expect(stage.at(t + VISIT - 200)?.mode).toBe('visiting')
  })

  it('walks at her pace along straight legs', () => {
    const stage = new ShioriStage()
    stage.update(world, quiet, 0)
    stage.update(world, quiet, LINGER)
    const a = stage.at(LINGER)!
    const b = stage.at(LINGER + 1000)!
    const moved = Math.abs(b.x - a.x) + Math.abs(b.y - a.y)
    expect(moved).toBeGreaterThan(0)
    expect(moved).toBeLessThanOrEqual(SHIORI_PACE + 1)
  })

  it('appears where she goes when motion is reduced', () => {
    const stage = new ShioriStage({ motion: false })
    stage.update(world, quiet, 0)
    stage.update(world, states([['Web-0', 'waiting'], ['Web-1', 'idle'], ['Identity-0', 'idle']]), 10)
    expect(stage.at(10)).toMatchObject({ mode: 'talking', talk: { deskId: 'Web-0', event: 'waiting' } })
  })
})
