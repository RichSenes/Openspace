import { describe, expect, it } from 'vitest'
import { defaultMergeSource, defaultMergeTarget, mergeBlocker, remoteStanding } from './mergeTarget'

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

describe('defaultMergeSource', () => {
  it('brings in the branch you have checked out, such as beta into main', () => {
    expect(defaultMergeSource({ current: 'beta', branches: ['beta', 'main'] }, 'main')).toBe('beta')
  })

  it('takes the first other branch when the checked out one is the target, and nothing alone', () => {
    expect(defaultMergeSource({ current: 'main', branches: ['beta', 'develop', 'main'] }, 'main')).toBe('beta')
    expect(defaultMergeSource({ current: 'main', branches: ['main'] }, 'main')).toBe('')
    expect(defaultMergeSource(undefined, 'main')).toBe('')
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

describe('remoteStanding', () => {
  const list = (ahead: number, behind: number, upstream: string | null = 'origin/main') => ({
    current: 'main',
    branches: ['main'],
    remotes: ['origin'],
    tracking: [{ branch: 'main', upstream, ahead, behind }],
  })

  it('offers a push when the branch is ahead, and never when the remote has newer work', () => {
    expect(remoteStanding(list(2, 0), 'main')).toEqual({ text: 'main is 2 commits ahead of origin/main.', canPush: true })
    expect(remoteStanding(list(1, 1), 'main')?.canPush).toBe(false)
    expect(remoteStanding(list(0, 0), 'main')?.canPush).toBe(false)
    expect(remoteStanding(list(0, 0, null), 'main')?.canPush).toBe(true)
  })

  it('says nothing for a project with no remote', () => {
    expect(remoteStanding({ current: 'main', branches: ['main'], remotes: [] }, 'main')).toBeNull()
  })
})
