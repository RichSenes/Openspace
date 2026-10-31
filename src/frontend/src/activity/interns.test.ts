import { describe, expect, it } from 'vitest'
import type { DeskInterns, Intern } from '../api/types'
import { InternStage, internsByDesk, mergeInterns, PUFF_MS } from './interns'

const intern = (id: string): Intern => ({ id, what: `Task ${id}`, since: '2015-06-24T09:00:00Z' })
const at = (desk: string, ...ids: string[]) => new Map([[desk, ids.map(intern)]])

describe('desk interns', () => {
  it('keep the latest list of each desk, per office', () => {
    const one: DeskInterns = { officeId: 'o1', deskId: 'd1', taskId: 't1', interns: [intern('a')] }
    const list = mergeInterns(mergeInterns([one], { ...one, interns: [] }), { ...one, deskId: 'd2', officeId: 'o2' })
    expect(internsByDesk(list, 'o1').get('d1')).toEqual([])
    expect(internsByDesk(list, 'o1').has('d2')).toBe(false)
  })
})

describe('the intern stage', () => {
  it('shows interns already there when the page opens without a puff', () => {
    const stage = new InternStage()
    stage.update(at('d1', 'a'), 0, true)
    expect(stage.at('d1', 0)).toEqual([{ intern: intern('a'), spot: 0, visible: true }])
  })

  it('brings a new intern in a puff, showing it from the middle of the puff', () => {
    const stage = new InternStage()
    stage.update(new Map(), 0, true)
    stage.update(at('d1', 'a'), 1000, true)
    expect(stage.at('d1', 1000)[0]).toMatchObject({ visible: false, puff: 0 })
    expect(stage.at('d1', 1000 + PUFF_MS / 2)[0]).toMatchObject({ visible: true, puff: 2 })
    expect(stage.at('d1', 1000 + PUFF_MS)[0]).toEqual({ intern: intern('a'), spot: 0, visible: true })
  })

  it('sends a leaving intern away in a puff, then forgets it', () => {
    const stage = new InternStage()
    stage.update(at('d1', 'a'), 0, true)
    stage.update(at('d1'), 5000, true)
    expect(stage.at('d1', 5000)[0]).toMatchObject({ visible: true, puff: 0 })
    expect(stage.at('d1', 5000 + PUFF_MS / 2)[0]).toMatchObject({ visible: false, puff: 2 })
    expect(stage.at('d1', 5000 + PUFF_MS)).toEqual([])
  })

  it('without animation, interns come and go at once', () => {
    const stage = new InternStage()
    stage.update(new Map(), 0, false)
    stage.update(at('d1', 'a'), 1000, false)
    expect(stage.at('d1', 1000)).toEqual([{ intern: intern('a'), spot: 0, visible: true }])
    stage.update(at('d1'), 2000, false)
    expect(stage.at('d1', 2000)).toEqual([])
  })

  it('keeps each intern on its spot and draws two at most', () => {
    const stage = new InternStage()
    stage.update(at('d1', 'a', 'b', 'c'), 0, true)
    stage.update(at('d1', 'b', 'c'), 100, true)
    expect(stage.at('d1', 100 + PUFF_MS).map((s) => [s.intern.id, s.spot])).toEqual([['b', 1]])
    stage.update(at('d1', 'b', 'c', 'd'), 2000, true)
    expect(stage.at('d1', 2000 + PUFF_MS).map((s) => [s.intern.id, s.spot])).toEqual([['b', 1], ['d', 0]])
  })
})
