import { describe, expect, it } from 'vitest'
import { boardSections, countByKind, noteFromBoard, slugify, type BoardItem } from './board'

const items: BoardItem[] = [
  { id: '1', kind: 'idea', text: 'A queue per office' },
  { id: '2', kind: 'doubt', text: 'Who picks the order?' },
  { id: '3', kind: 'task', text: 'Build the queue', office: 'Backend' },
  { id: '4', kind: 'decision', text: 'Oldest first' },
  { id: '5', kind: 'idea', text: 'Tests again after merge' },
]

describe('the whiteboard', () => {
  it('groups items by kind, decisions first, and leaves empty kinds out', () => {
    const sections = boardSections(items)
    expect(sections.map((s) => s.kind)).toEqual(['decision', 'idea', 'doubt', 'task'])
    expect(sections[0].heading).toBe('Decision')
    expect(sections[1].heading).toBe('Ideas')
    expect(boardSections(items.filter((i) => i.kind === 'idea')).map((s) => s.kind)).toEqual(['idea'])
  })

  it('counts items by kind', () => {
    expect(countByKind(items)).toEqual({ idea: 2, doubt: 1, decision: 1, task: 1 })
  })

  it('makes file names from titles', () => {
    expect(slugify('Merge queue per office!')).toBe('merge-queue-per-office')
    expect(slugify('  Perché è così  ')).toBe('perche-e-cosi')
  })

  it('becomes a session note with tasks as checkboxes and sources once each', () => {
    const note = noteFromBoard(items, 'Merge queue', '2026-11-21', ['Decisions/a', 'Decisions/a', 'Rooms/b'])
    expect(note.path).toBe('Sessions/2026-11-21-merge-queue.md')
    expect(note.links).toEqual(['Decisions/a', 'Rooms/b'])
    expect(note.markdown).toContain('## Decision\n\n- Oldest first')
    expect(note.markdown).toContain('- [ ] Build the queue (Backend)')
    expect(note.markdown).toContain('## Sources\n\n- [[Decisions/a]]\n- [[Rooms/b]]\n')
    expect(note.markdown.indexOf('## Ideas')).toBeLessThan(note.markdown.indexOf('## Task'))
  })
})
