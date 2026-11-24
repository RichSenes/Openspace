import { describe, expect, it } from 'vitest'
import type { WorldCharacter, WorldView } from '../../../api/types'
import { specsOf } from './fromWorld'

const character = (kind: WorldCharacter['kind'], name: string): WorldCharacter => ({ id: name, kind, name, state: 'Free', doing: null, taskId: null, taskTitle: null, forDesk: null, traits: [] })

describe('the lab from the real world', () => {
  it('makes an area per office with a seat for each desk and each routine, interns not counted', () => {
    const world: WorldView = {
      offices: [
        { id: '1', name: 'Karuta', characters: [character('Desk', 'Frontend'), character('Desk', 'Backend'), character('Intern', 'Intern'), character('Routine', 'Release notes')] },
        { id: '2', name: 'Riku', characters: [character('Routine', 'Job offers'), character('Routine', 'Paper')] },
        { id: '3', name: 'Empty', characters: [] },
      ],
      waiting: [],
    }

    const specs = specsOf(world)

    expect(specs.map((s) => [s.name, s.agents, s.flavor])).toEqual([
      ['Karuta', 3, 'code'],
      ['Riku', 2, 'data'],
      ['Empty', 1, 'code'],
    ])
    expect(new Set(specs.map((s) => s.accent)).size).toBe(3)
  })
})
