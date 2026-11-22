import { describe, expect, it } from 'vitest'
import type { Graph, GraphNote } from './types'
import {
  authorsOf,
  bounds,
  cityLayout,
  createSimulation,
  degrees,
  filterGraph,
  foldersOfGraph,
  hash,
  MIN_ALPHA,
  nearest,
  neighbours,
  rowsOf,
  settle,
  step,
} from './graph'

const n = (path: string, kind: string | null = 'note', author: string | null = null): GraphNote => ({
  path,
  title: path,
  kind,
  status: null,
  offices: [],
  author,
})

const graph: Graph = {
  notes: [
    n('Constitution.md', 'rule', 'person'),
    n('Decisions/a.md', 'decision', 'shiori'),
    n('Decisions/b.md', 'decision', 'darlene'),
    n('Plans/release.md', 'plan', 'shiori'),
    n('Studies/alone.md', 'study'),
  ],
  links: [
    { from: 'Decisions/a.md', to: 'Plans/release.md' },
    { from: 'Decisions/b.md', to: 'Decisions/a.md' },
    { from: 'Plans/release.md', to: 'Constitution.md' },
  ],
}

describe('map filters', () => {
  it('keeps the notes of a folder and only the links inside it', () => {
    const decisions = filterGraph(graph, { folder: 'Decisions' })
    expect(decisions.notes.map((x) => x.path)).toEqual(['Decisions/a.md', 'Decisions/b.md'])
    expect(decisions.links).toEqual([{ from: 'Decisions/b.md', to: 'Decisions/a.md' }])
    expect(filterGraph(graph, { folder: '' }).notes.map((x) => x.path)).toEqual(['Constitution.md'])
  })

  it('filters by kind and author together', () => {
    expect(filterGraph(graph, { kind: 'decision', author: 'shiori' }).notes.map((x) => x.path)).toEqual(['Decisions/a.md'])
    expect(filterGraph(graph, {}).links).toHaveLength(3)
  })

  it('lists folders with the root first, and authors', () => {
    expect(foldersOfGraph(graph.notes)).toEqual(['', 'Decisions', 'Plans', 'Studies'])
    expect(authorsOf(graph.notes)).toEqual(['darlene', 'person', 'shiori'])
  })

  it("knows each note's links both ways", () => {
    const map = neighbours(graph.links)
    expect(map.get('Decisions/a.md')).toEqual({ out: ['Plans/release.md'], in: ['Decisions/b.md'] })
    expect(map.get('Studies/alone.md')).toBeUndefined()
    expect(degrees(graph.links).get('Plans/release.md')).toBe(2)
  })
})

describe('the force layout', () => {
  it('is deterministic: the same notes settle in the same places', () => {
    const a = settle(createSimulation(graph.notes, graph.links))
    const b = settle(createSimulation(graph.notes, graph.links))
    expect([...a.x]).toEqual([...b.x])
    expect([...a.y]).toEqual([...b.y])
  })

  it('steps the same way every time and stops once cooled', () => {
    const a = createSimulation(graph.notes, graph.links)
    const b = createSimulation(graph.notes, graph.links)
    for (let i = 0; i < 5; i++) {
      step(a)
      step(b)
    }
    expect([...a.x]).toEqual([...b.x])
    settle(a)
    expect(a.alpha).toBeLessThan(MIN_ALPHA)
    const before = [...a.x]
    expect(step(a)).toBe(false)
    expect([...a.x]).toEqual(before)
  })

  it('keeps linked notes closer than unlinked ones and every position finite', () => {
    const sim = settle(createSimulation(graph.notes, graph.links))
    const d = (i: number, j: number) => Math.hypot(sim.x[i] - sim.x[j], sim.y[i] - sim.y[j])
    expect(d(1, 3)).toBeLessThan(d(4, 3))
    expect([...sim.x, ...sim.y].every(Number.isFinite)).toBe(true)
  })

  it('parts notes that start on the same spot', () => {
    const sim = createSimulation([n('a.md'), n('b.md')], [])
    sim.x[1] = sim.x[0]
    sim.y[1] = sim.y[0]
    step(sim)
    expect(sim.x[0]).not.toBe(sim.x[1])
  })

  it('takes a few milliseconds a step for 500 notes', () => {
    const many = Array.from({ length: 500 }, (_, i) => n(`F${i % 7}/note-${i}.md`))
    const links = many.slice(1).map((x, i) => ({ from: x.path, to: many[i].path }))
    const sim = createSimulation(many, links)
    step(sim)
    const started = performance.now()
    for (let i = 0; i < 10; i++) step(sim)
    expect((performance.now() - started) / 10).toBeLessThan(8)
  })

  it('frames the notes', () => {
    expect(bounds([1, -2, 3], [0, 5, -1])).toEqual({ minX: -2, minY: -1, maxX: 3, maxY: 5 })
    expect(bounds([], [])).toEqual({ minX: -1, minY: -1, maxX: 1, maxY: 1 })
  })

  it('finds the note under the pointer', () => {
    expect(nearest([0, 10, 20], [0, 0, 0], 11, 1, 4)).toBe(1)
    expect(nearest([0, 10, 20], [0, 0, 0], 50, 0, 4)).toBe(-1)
  })
})

describe('the city', () => {
  it('puts each folder in its own block, buildings taller with more links', () => {
    const city = cityLayout(graph.notes, graph.links)
    expect(city.blocks.map((b) => b.folder)).toEqual(['', 'Decisions', 'Plans', 'Studies'])
    const [root, decisions] = city.blocks
    expect(decisions.x).toBeGreaterThan(root.x + root.width)
    // Decisions/a.md stands inside its block.
    expect(city.x[1]).toBeGreaterThan(decisions.x)
    expect(city.x[1]).toBeLessThan(decisions.x + decisions.width)
    expect([...city.floors]).toEqual([2, 3, 2, 3, 1])
  })

  it('groups buildings by the line they stand on, back row first', () => {
    expect(rowsOf([20, 10, 20, 5])).toEqual([[3], [1], [0, 2]])
    expect(rowsOf([])).toEqual([])
  })

  it('is the same every time', () => {
    expect([...cityLayout(graph.notes, graph.links).y]).toEqual([...cityLayout(graph.notes, graph.links).y])
    expect(hash('a')).toBe(hash('a'))
    expect(hash('a')).not.toBe(hash('b'))
  })
})
