import { describe, expect, it } from 'vitest'
import { mergeForExit, type Shown } from './exitList'

const key = (s: string) => s.split(':')[0]

describe('mergeForExit', () => {
  it('adds new items and keeps vanished ones as leaving', () => {
    const shown: Shown<string>[] = [{ key: 'a', item: 'a:old', leaving: false }]

    const next = mergeForExit(shown, ['b:new'], key)

    expect(next).toEqual([
      { key: 'a', item: 'a:old', leaving: true },
      { key: 'b', item: 'b:new', leaving: false },
    ])
  })

  it('refreshes items still there and brings back one that returns while leaving', () => {
    const shown: Shown<string>[] = [{ key: 'a', item: 'a:old', leaving: true }]

    expect(mergeForExit(shown, ['a:new'], key)).toEqual([{ key: 'a', item: 'a:new', leaving: false }])
  })
})
