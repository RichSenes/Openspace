import { describe, expect, it } from 'vitest'
import { openPage, setPageOpener } from './pages'

describe('openPage', () => {
  it('opens the page over the lab, where it lands included', () => {
    const asked: unknown[] = []
    const stop = setPageOpener((view, at) => asked.push([view, at]))
    openPage('board', { officeId: 'o1', taskId: 't1' })
    stop()
    expect(asked).toEqual([['board', { officeId: 'o1', taskId: 't1' }]])
  })

  it('stops only the opener it was given', () => {
    const first: string[] = []
    const second: string[] = []
    const stopFirst = setPageOpener((view) => first.push(view))
    const stopSecond = setPageOpener((view) => second.push(view))
    stopFirst()
    openPage('brain')
    stopSecond()
    expect(first).toEqual([])
    expect(second).toEqual(['brain'])
  })
})
