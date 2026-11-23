import { describe, expect, it } from 'vitest'
import { RATE, Utterances, resample, toWav, wakeLabel } from './wake'

const tone = (ms: number, amp: number) => Float32Array.from({ length: (RATE * ms) / 1000 }, (_, i) => amp * Math.sin(i / 3))
const join = (...parts: Float32Array[]) => {
  const out = new Float32Array(parts.reduce((n, p) => n + p.length, 0))
  let at = 0
  for (const p of parts) {
    out.set(p, at)
    at += p.length
  }
  return out
}

describe('cutting what is said into sentences', () => {
  it('a voice between two silences is one sentence, with a little of the quiet before it', () => {
    const cut = new Utterances()
    const said = cut.push(join(tone(1000, 0.001), tone(1200, 0.3), tone(1000, 0.001)))
    expect(said).toHaveLength(1)
    // 1.2 s of voice, plus up to 300 ms before and the 700 ms pause that ended it.
    expect(said[0].length / RATE).toBeGreaterThan(1.8)
    expect(said[0].length / RATE).toBeLessThan(2.3)
  })

  it('arrives in pieces as small as the browser gives them', () => {
    const cut = new Utterances()
    const all = join(tone(500, 0.001), tone(800, 0.3), tone(900, 0.001))
    const said: Float32Array[] = []
    for (let i = 0; i < all.length; i += 1000) said.push(...cut.push(all.slice(i, i + 1000)))
    expect(said).toHaveLength(1)
  })

  it('a click is not a sentence, and nor is the room’s own noise', () => {
    const cut = new Utterances()
    expect(cut.push(join(tone(500, 0.002), tone(100, 0.4), tone(1000, 0.002)))).toHaveLength(0)
    expect(cut.push(tone(5000, 0.005))).toHaveLength(0)
  })

  it('two sentences with a pause between them come apart', () => {
    const cut = new Utterances()
    const said = cut.push(join(tone(600, 0.3), tone(900, 0.001), tone(600, 0.3), tone(900, 0.001)))
    expect(said).toHaveLength(2)
  })

  it('a long talk in the room is cut, not kept forever', () => {
    const cut = new Utterances()
    expect(cut.push(tone(13_000, 0.3)).length).toBeGreaterThanOrEqual(1)
  })
})

describe('sound for the listening server', () => {
  it('resamples 48 kHz to 16 kHz', () => {
    expect(resample(new Float32Array(4800), 48_000)).toHaveLength(1600)
    const same = new Float32Array(10)
    expect(resample(same, RATE)).toBe(same)
  })

  it('writes a 16-bit mono WAV', () => {
    const wav = new DataView(toWav(Float32Array.from([0, 1, -1, 2])))
    expect(String.fromCharCode(wav.getUint8(0), wav.getUint8(1), wav.getUint8(2), wav.getUint8(3))).toBe('RIFF')
    expect(wav.getUint32(24, true)).toBe(RATE)
    expect(wav.getUint32(40, true)).toBe(8)
    expect(wav.getInt16(46, true)).toBe(0x7fff)
    expect(wav.getInt16(48, true)).toBe(-0x7fff)
    expect(wav.getInt16(50, true)).toBe(0x7fff)
  })

  it('says what she is doing, and nothing when off', () => {
    expect(wakeLabel('waiting')).toContain('Shiori')
    expect(wakeLabel('off')).toBe('')
  })
})

describe('remembering the choice', () => {
  it('is off until turned on, also where the browser keeps nothing', async () => {
    const { loadByName, saveByName } = await import('./wake')
    expect(() => saveByName(true)).not.toThrow()
    expect(loadByName()).toBe(false)
  })
})
