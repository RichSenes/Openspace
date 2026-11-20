import { describe, expect, it } from 'vitest'
import { boardColumns } from './columnOrder'
import { COLUMNS } from './taskRules'

describe('boardColumns', () => {
  it('keeps the order of the work on wide screens', () => {
    expect(boardColumns(false)).toBe(COLUMNS)
  })

  it('puts the columns that need you first on narrow screens', () => {
    expect(boardColumns(true).map((c) => c.status)).toEqual(['Review', 'Failed', 'Proposed', 'Queued', 'Running', 'Done'])
  })

  it('leaves no column out and repeats none', () => {
    const statuses = boardColumns(true).map((c) => c.status)
    expect(new Set(statuses).size).toBe(COLUMNS.length)
  })
})
