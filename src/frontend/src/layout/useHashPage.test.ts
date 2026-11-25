import { describe, expect, it } from 'vitest'
import { closeStep, openStep } from './useHashPage'

describe('opening a page over the lab', () => {
  it('takes a step from the lab, so back returns to it', () => {
    expect(openStep(null, 'board')).toBe('push')
  })

  it('swaps one page for another without a new step', () => {
    expect(openStep('board', 'brain')).toBe('replace')
  })

  it('does nothing for the page already open', () => {
    expect(openStep('board', 'board')).toBe('none')
  })
})

describe('closing it', () => {
  it('steps back when the page was opened from the lab', () => {
    expect(closeStep('board', true)).toBe('back')
  })

  it('clears the address when the visit started on the page', () => {
    expect(closeStep('board', false)).toBe('replace')
  })

  it('does nothing over the lab', () => {
    expect(closeStep(null, true)).toBe('none')
  })
})
