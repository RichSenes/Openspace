import { describe, expect, it } from 'vitest'
import { placeOf } from './useVoice'

const line = (from: number, to: number, text = 'x'.repeat(28)) => ({ text, from, to, language: 'it' })

describe('where in the text her voice is', () => {
  it('is nowhere before she says anything', () => {
    expect(placeOf([], 3)).toBeNull()
  })

  it('waits at the first words until they are heard', () => {
    expect(placeOf([{ line: line(10, 50), start: 5, seconds: 2, arrived: true }], 4)).toBe(10)
  })

  it('moves through a line as it plays, and on to the next', () => {
    const heard = [
      { line: line(0, 40), start: 1, seconds: 2, arrived: true },
      { line: line(40, 80), start: 3, seconds: 4, arrived: true },
    ]
    expect(placeOf(heard, 2)).toBe(20)
    expect(placeOf(heard, 5)).toBe(60)
    expect(placeOf(heard, 9)).toBe(80)
  })

  it('guesses how long a line lasts while it is still arriving', () => {
    // 28 characters at her pace last two seconds, though only half a second has arrived.
    expect(placeOf([{ line: line(0, 28), start: 0, seconds: 0.5, arrived: false }], 1)).toBe(14)
  })
})
