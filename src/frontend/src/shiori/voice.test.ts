import { describe, expect, it } from 'vitest'
import { afterHearing, earsLabel, formatDuration, isTalkKey, loudness, pickMime } from './voice'

describe('talking to Shiori', () => {
  it('sends what was said when the composer is empty', () => {
    expect(afterHearing({ text: ' Ciao Shiori ', language: 'it', seconds: 1 }, '')).toEqual({ send: 'Ciao Shiori', compose: '' })
  })

  it('adds to what was typed instead of sending it', () => {
    expect(afterHearing({ text: 'and the tests', language: 'en', seconds: 1 }, 'Check the cart ')).toEqual({ send: null, compose: 'Check the cart and the tests' })
  })

  it('does nothing with silence', () => {
    expect(afterHearing({ text: '  ', language: 'en', seconds: 1 }, 'kept')).toEqual({ send: null, compose: 'kept' })
  })

  it('prefers opus in webm, and falls back', () => {
    expect(pickMime(() => true)).toBe('audio/webm;codecs=opus')
    expect(pickMime((m) => m === 'audio/mp4')).toBe('audio/mp4')
    expect(pickMime(() => false)).toBeUndefined()
  })

  it('starts talking with the space bar only outside fields and not on repeat', () => {
    const div = { tagName: 'DIV' } as unknown as EventTarget
    const area = { tagName: 'TEXTAREA' } as unknown as EventTarget
    expect(isTalkKey({ code: 'Space', repeat: false, target: div }, false)).toBe(true)
    expect(isTalkKey({ code: 'Space', repeat: true, target: div }, false)).toBe(false)
    expect(isTalkKey({ code: 'Space', repeat: false, target: area }, false)).toBe(false)
    expect(isTalkKey({ code: 'KeyA', repeat: false, target: div }, false)).toBe(false)
    expect(isTalkKey({ code: 'Space', repeat: false, target: div }, true)).toBe(false)
  })

  it('says what is happening, with the time while recording', () => {
    expect(earsLabel('recording', 65_000)).toContain('1:05')
    expect(earsLabel('denied', 0)).toContain('blocked')
    expect(formatDuration(9_999)).toBe('0:09')
  })

  it('measures loudness from silence to a loud frame', () => {
    expect(loudness(new Uint8Array(64).fill(128))).toBe(0)
    const loud = new Uint8Array(64).map((_, i) => (i % 2 ? 255 : 0))
    expect(loudness(loud)).toBe(1)
  })
})
