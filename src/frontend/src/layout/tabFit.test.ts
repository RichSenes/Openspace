import { describe, expect, it } from 'vitest'
import { fitTabs } from './tabFit'

const tabs = [100, 100, 100, 100, 100]

describe('fitTabs', () => {
  it('shows every tab and no More when they all fit', () => {
    expect(fitTabs(tabs, 500, 80, 0)).toEqual({ shown: [0, 1, 2, 3, 4], more: [] })
  })

  it('keeps room for More once one tab does not fit', () => {
    // 450 px: without More four would fit, with More (80) only three.
    expect(fitTabs(tabs, 450, 80, 0)).toEqual({ shown: [0, 1, 2], more: [3, 4] })
  })

  it('keeps the current tab in sight, in place of the last tabs that fit', () => {
    expect(fitTabs(tabs, 380, 80, 4)).toEqual({ shown: [0, 1, 4], more: [2, 3] })
  })

  it('leaves a current tab that already fits where it is', () => {
    expect(fitTabs(tabs, 380, 80, 1)).toEqual({ shown: [0, 1, 2], more: [3, 4] })
  })

  it('shows the current tab alone when the bar has room for nothing else', () => {
    expect(fitTabs(tabs, 150, 80, 3)).toEqual({ shown: [3], more: [0, 1, 2, 4] })
  })

  it('makes room for a current tab wider than the one it replaces', () => {
    expect(fitTabs([100, 100, 100, 180], 380, 80, 3)).toEqual({ shown: [0, 3], more: [1, 2] })
  })

  it('puts everything behind More when there is no current tab and no room', () => {
    expect(fitTabs(tabs, 60, 80, -1)).toEqual({ shown: [], more: [0, 1, 2, 3, 4] })
  })
})
