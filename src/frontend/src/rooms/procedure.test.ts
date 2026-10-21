import { describe, expect, it } from 'vitest'
import { formatSteps, parseSteps } from './procedure'

describe('parseSteps', () => {
  it('takes one step per line and drops blanks and list markers', () => {
    expect(parseSteps('1. Back up first\n\n - Log the change \n* Tell the team')).toEqual([
      'Back up first',
      'Log the change',
      'Tell the team',
    ])
  })

  it('round-trips with formatSteps', () => {
    const steps = ['Back up first', 'Log the change']
    expect(parseSteps(formatSteps(steps))).toEqual(steps)
  })
})
