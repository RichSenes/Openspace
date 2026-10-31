import { describe, expect, it } from 'vitest'
import { chance, errandOf, REACH, routeTo, WalkStage, type DeskDoing, type DeskPlace, type StationSpot } from './walks'

const shelf: StationSpot = { x: 100, y: 60, errand: 'documents', facing: 'back' }
const rack: StationSpot = { x: 20, y: 60, errand: 'commands', facing: 'back' }
const coffee: StationSpot = { x: 130, y: 120, errand: 'break', facing: 'front' }

const place = (deskId: string, x: number, stations: StationSpot[] = [shelf, rack, coffee]): DeskPlace => ({
  deskId,
  roomId: 'room',
  seat: { x, y: 80 },
  exits: [{ x: x - 22, y: 80 }, { x: x + 30, y: 80 }],
  stations,
})

const doing = (entries: [string, DeskDoing][]) => new Map(entries)

/** Steps of reading that, by chance, send the desk to a station. */
const tempting = (deskId: string) => Array.from({ length: 60 }, (_, i) => `Reading file${i}.ts`).filter((d) => chance(`${deskId}:${d}`) < 0.35)

describe('errandOf', () => {
  it('sends work to the furniture for it and idle agents to a break', () => {
    expect(errandOf({ state: 'reading', doing: 'Reading header.php' })).toBe('documents')
    expect(errandOf({ state: 'working', doing: 'Searching the code' })).toBe('documents')
    expect(errandOf({ state: 'working', doing: 'Running the tests' })).toBe('commands')
    expect(errandOf({ state: 'working', doing: 'Editing header.php' })).toBeNull()
    expect(errandOf({ state: 'done' })).toBe('break')
    expect(errandOf({ state: 'waiting', doing: 'Reading x' })).toBeNull()
    expect(errandOf({ state: 'queued' })).toBeNull()
  })
})

describe('WalkStage', () => {
  it('sends a reading agent now and then to the shelf, never to the rack', () => {
    const stage = new WalkStage()
    const step = tempting('a')[0]
    stage.update([place('a', 60)], doing([['a', { state: 'reading', doing: step }]]), 0)
    expect(stage.at('a', 0)).toMatchObject({ x: 60, y: 80 })
    stage.update([place('a', 60)], doing([['a', { state: 'reading', doing: step }]]), 10_000)
    expect(stage.at('a', 10_000)).toMatchObject({ x: shelf.x, y: shelf.y, facing: 'back' })
  })

  it('leaves most steps at the desk', () => {
    const stage = new WalkStage()
    const calm = Array.from({ length: 60 }, (_, i) => `Reading file${i}.ts`).find((d) => chance(`a:${d}`) >= 0.35)!
    stage.update([place('a', 60)], doing([['a', { state: 'reading', doing: calm }]]), 0)
    expect(stage.at('a', 0)).toBeUndefined()
  })

  it('hurries back to the desk when it needs you, and sits down', () => {
    const stage = new WalkStage()
    const p = place('a', 60)
    stage.update([p], doing([['a', { state: 'reading', doing: tempting('a')[0] }]]), 0)
    stage.update([p], doing([['a', { state: 'reading', doing: tempting('a')[0] }]]), 10_000)
    stage.update([p], doing([['a', { state: 'waiting' }]]), 11_000)
    expect(stage.at('a', 11_500)!.step).toBeGreaterThanOrEqual(0)
    stage.update([p], doing([['a', { state: 'waiting' }]]), 20_000)
    expect(stage.at('a', 20_000)).toBeUndefined()
  })

  it('goes back once the step is something else, after a little while', () => {
    const stage = new WalkStage()
    const p = place('a', 60)
    const step = tempting('a')[0]
    stage.update([p], doing([['a', { state: 'reading', doing: step }]]), 0)
    stage.update([p], doing([['a', { state: 'reading', doing: step }]]), 10_000)
    stage.update([p], doing([['a', { state: 'working', doing: 'Editing a.ts' }]]), 11_000)
    expect(stage.at('a', 11_000)).toMatchObject({ x: shelf.x, y: shelf.y })
    stage.update([p], doing([['a', { state: 'working', doing: 'Editing a.ts' }]]), 15_000)
    stage.update([p], doing([['a', { state: 'working', doing: 'Editing a.ts' }]]), 30_000)
    expect(stage.at('a', 30_000)).toBeUndefined()
  })

  it('never walks farther than a stroll', () => {
    const far: StationSpot = { ...shelf, x: 600 }
    const stage = new WalkStage()
    const p = place('a', 60, [far])
    expect(routeTo(p, far).length).toBe(4)
    for (const step of tempting('a')) stage.update([p], doing([['a', { state: 'reading', doing: step }]]), 0)
    expect(stage.at('a', 0)).toBeUndefined()
    expect(REACH).toBeLessThan(600)
  })

  it('takes a break now and then when there is nothing to do', () => {
    const stage = new WalkStage()
    const p = place('a', 60)
    let breaks = 0
    for (let t = 0; t < 40 * 150_000; t += 5_000) {
      stage.update([p], doing([['a', { state: 'idle' }]]), t)
      if (stage.at('a', t)?.x === coffee.x && stage.at('a', t)?.y === coffee.y) breaks++
    }
    expect(breaks).toBeGreaterThan(0)
  })

  it('lets two agents at the same station chat, taking turns', () => {
    const stage = new WalkStage()
    const near: StationSpot = { ...coffee }
    const places = [place('a', 60, [near]), place('b', 70, [{ ...near, x: near.x + 8 }])]
    const idle = doing([['a', { state: 'done' }], ['b', { state: 'done' }]])
    let both = -1
    for (let t = 0; t < 40 * 150_000 && both < 0; t += 1_000) {
      stage.update(places, idle, t)
      if (stage.at('a', t)?.chat && stage.at('b', t)?.chat) both = t
    }
    expect(both).toBeGreaterThanOrEqual(0)
    const [a, b] = [stage.at('a', both)!, stage.at('b', both)!]
    expect(a.chat!.speaking).not.toBe(b.chat!.speaking)
  })
})
