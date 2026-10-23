import { describe, expect, it } from 'vitest'
import { bubbleText, placeBubbles, type BubbleBox } from './bubbles'

const box = (deskId: string, anchorX: number, anchorY = 100, urgent = false): BubbleBox => ({ deskId, anchorX, anchorY, width: 60, height: 20, urgent })

describe('placeBubbles', () => {
  it('puts a lone bubble right above its desk', () => {
    expect(placeBubbles([box('a', 50)])[0]).toMatchObject({ left: 20, top: 76 })
  })

  it('lifts a bubble that would cover its neighbour above it', () => {
    const [first, second] = placeBubbles([box('a', 50), box('b', 80)])
    expect(first.top).toBe(76)
    expect(second.top).toBe(76 - 20 - 2)
  })

  it('keeps a waiting agent closest to its desk', () => {
    const placed = placeBubbles([box('working', 50), box('waiting', 80, 100, true)])
    expect(placed.find((p) => p.deskId === 'waiting')?.top).toBe(76)
    expect(placed.find((p) => p.deskId === 'working')?.top).toBeLessThan(76)
  })

  it('leaves bubbles far apart where they are', () => {
    const placed = placeBubbles([box('a', 50), box('b', 300)])
    expect(placed.map((p) => p.top)).toEqual([76, 76])
  })
})

describe('bubbleText', () => {
  it('cuts long sentences at a word', () => {
    expect(bubbleText('Adding the logout button to the header of every page of the shop', 30)).toBe('Adding the logout button to…')
    expect(bubbleText('  Running   the tests ')).toBe('Running the tests')
  })
})
