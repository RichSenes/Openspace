import { describe, expect, it } from 'vitest'
import { roomChanges, roomFormOf } from './roomForm'

const room = { context: 'Use pnpm.', procedure: ['Back up the file', 'Note it in CHANGES.md'], check: 'npm test' }

describe('roomChanges', () => {
  it('finds nothing to save in the room as it is, or written the same way again', () => {
    expect(roomChanges(room, roomFormOf(room))).toEqual([])
    expect(roomChanges(room, { ...roomFormOf(room), procedure: '1. Back up the file\n\n- Note it in CHANGES.md', check: ' npm test ' })).toEqual([])
  })

  it('names each part that changed, to save them together', () => {
    expect(roomChanges(room, { context: 'Use npm.', procedure: 'Back up the file', check: '' })).toEqual(['context', 'procedure', 'check'])
  })

  it('reads a room without a check as an empty field', () => {
    expect(roomFormOf({ ...room, check: null }).check).toBe('')
  })
})
