import { describe, expect, it } from 'vitest'
import { type Heard, placeOf, voicedFrames, voiceIn } from './useVoice'

const line = (from: number, to: number, text = 'x'.repeat(30)) => ({ text, from, to, language: 'it' })
/** A piece of `pattern` frames of 0.1 s: 1 voice, 0 silence. */
const piece = (start: number, pattern: string) => ({ start, frame: 0.1, voiced: [...pattern].map((c) => c === '1') })

describe('where in the text her voice is', () => {
  it('is nowhere before she says anything, and at the first words until they are heard', () => {
    expect(placeOf([], 3, 15)).toBeNull()
    const heard: Heard[] = [{ line: line(10, 50), pieces: [piece(5, '1111')], arrived: true }]
    expect(placeOf(heard, 4, 15)).toBe(10)
  })

  it('moves only while she speaks, and waits at her pauses', () => {
    // Two words, a pause, two words: half the text by the pause, still half during it.
    const heard: Heard[] = [{ line: line(0, 40), pieces: [piece(0, '110011')], arrived: true }]
    expect(placeOf(heard, 0.2, 15)).toBeCloseTo(20)
    expect(placeOf(heard, 0.35, 15)).toBeCloseTo(20)
    expect(placeOf(heard, 0.5, 15)).toBeCloseTo(30)
    expect(placeOf(heard, 9, 15)).toBeCloseTo(40)
  })

  it('goes on to the next line', () => {
    const heard: Heard[] = [
      { line: line(0, 40), pieces: [piece(0, '11')], arrived: true },
      { line: line(40, 80), pieces: [piece(0.2, '1111')], arrived: true },
    ]
    expect(placeOf(heard, 0.4, 15)).toBeCloseTo(60)
  })

  it('guesses a line still arriving from her pace, without reaching its end', () => {
    // 30 characters at 15 a second of voice last two seconds; half a second has arrived.
    const heard: Heard[] = [{ line: line(0, 30), pieces: [piece(0, '11111')], arrived: false }]
    expect(placeOf(heard, 0.5, 15)).toBeCloseTo(7.5)
    expect(placeOf(heard, 9, 15)).toBeLessThan(30)
  })
})

describe('voice and silence in her audio', () => {
  it('tells loud frames from quiet ones', () => {
    const samples = new Float32Array(1600)
    samples.fill(0.2, 0, 320) // one frame of 20 ms at 16 kHz
    expect(voicedFrames(samples, 16000)).toEqual([true, false, false, false, false])
  })

  it('counts the seconds of voice up to a moment', () => {
    const h: Heard = { line: line(0, 10), pieces: [piece(0, '101'), piece(0.3, '1')], arrived: true }
    expect(voiceIn(h)).toBeCloseTo(0.3)
    expect(voiceIn(h, 0.25)).toBeCloseTo(0.15)
  })
})
