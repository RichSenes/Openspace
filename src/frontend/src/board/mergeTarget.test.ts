import { describe, expect, it } from 'vitest'
import { defaultMergeTarget } from './mergeTarget'

describe('defaultMergeTarget', () => {
  it('prefers the branch you have checked out', () => {
    expect(defaultMergeTarget({ current: 'develop', branches: ['develop', 'main'] })).toBe('develop')
  })

  it('falls back to main or master when detached', () => {
    expect(defaultMergeTarget({ current: null, branches: ['develop', 'master'] })).toBe('master')
  })

  it('takes the first branch otherwise, and nothing when there are none', () => {
    expect(defaultMergeTarget({ current: null, branches: ['release'] })).toBe('release')
    expect(defaultMergeTarget({ current: null, branches: [] })).toBe('')
    expect(defaultMergeTarget(undefined)).toBe('')
  })
})
