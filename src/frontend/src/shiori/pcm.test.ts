import { describe, expect, it } from 'vitest'
import { join, Pcm } from './pcm'

describe('her voice as it arrives', () => {
  it('turns 16-bit samples into numbers between -1 and 1', () => {
    const samples = new Pcm().push(new Uint8Array([0x00, 0x40, 0x00, 0xc0, 0x00, 0x00]))
    expect(Array.from(samples)).toEqual([0.5, -0.5, 0])
  })

  it('keeps a sample split between two pieces for the next one', () => {
    const pcm = new Pcm()
    expect(Array.from(pcm.push(new Uint8Array([0x00, 0x40, 0x00])))).toEqual([0.5])
    expect(Array.from(pcm.push(new Uint8Array([0xc0])))).toEqual([-0.5])
    expect(pcm.push(new Uint8Array([]))).toHaveLength(0)
  })

  it('joins pieces in order', () => {
    expect(Array.from(join([new Float32Array([1]), new Float32Array([]), new Float32Array([2, 3])]))).toEqual([1, 2, 3])
  })
})
