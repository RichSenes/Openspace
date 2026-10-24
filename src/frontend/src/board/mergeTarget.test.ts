import { describe, expect, it } from 'vitest'
import { defaultMergeTarget, mergeBlocker } from './mergeTarget'

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

describe('mergeBlocker', () => {
  const list = { current: 'beta', branches: ['beta', 'main'], uncommittedChanges: 11 }

  it('warns before merging into a checkout with uncommitted changes', () => {
    expect(mergeBlocker(list, 'beta')).toContain('11 uncommitted changes')
  })

  it('lets other branches and a clean checkout through', () => {
    expect(mergeBlocker(list, 'main')).toBeNull()
    expect(mergeBlocker({ ...list, uncommittedChanges: 0 }, 'beta')).toBeNull()
    expect(mergeBlocker(undefined, 'beta')).toBeNull()
  })
})
