import { describe, expect, it } from 'vitest'
import { blendLook, FACE_LOOKS, faceReducer, mixHex, type FaceEvent, type FaceState } from './faceMachine'

const run = (events: FaceEvent[], from: FaceState = 'idle') => events.reduce(faceReducer, from)

describe("Shiori's face machine", () => {
  it('goes through a whole exchange: listen, think, answer, feel, rest', () => {
    expect(run([{ type: 'hear' }])).toBe('listening')
    expect(run([{ type: 'hear' }, { type: 'send' }])).toBe('thinking')
    expect(run([{ type: 'hear' }, { type: 'send' }, { type: 'speak' }])).toBe('speaking')
    expect(run([{ type: 'send' }, { type: 'speak' }, { type: 'finish', mood: 'unsure' }])).toBe('unsure')
    expect(run([{ type: 'send' }, { type: 'speak' }, { type: 'finish', mood: 'pleased' }, { type: 'settle' }])).toBe('idle')
  })

  it('ends a neutral answer at rest, with no feeling shown', () => {
    expect(run([{ type: 'speak' }, { type: 'finish', mood: 'neutral' }])).toBe('idle')
  })

  it('does not let typing interrupt thinking or speaking', () => {
    expect(faceReducer('speaking', { type: 'hear' })).toBe('speaking')
    expect(faceReducer('thinking', { type: 'hear' })).toBe('thinking')
    expect(faceReducer('pleased', { type: 'hear' })).toBe('listening')
  })

  it('ignores a finish that comes without an answer', () => {
    expect(faceReducer('idle', { type: 'finish', mood: 'pleased' })).toBe('idle')
    expect(faceReducer('thinking', { type: 'settle' })).toBe('thinking')
  })

  it('resets from any state', () => {
    expect(faceReducer('unsure', { type: 'reset' })).toBe('idle')
  })

  it('blends two looks without jumping', () => {
    const half = blendLook(FACE_LOOKS.idle, FACE_LOOKS.thinking, 0.5)
    expect(half.energy).toBeCloseTo((FACE_LOOKS.idle.energy + FACE_LOOKS.thinking.energy) / 2)
    expect(blendLook(FACE_LOOKS.idle, FACE_LOOKS.thinking, 2).color).toBe(FACE_LOOKS.thinking.color)
    expect(mixHex('#000000', '#ffffff', 0.5)).toBe('#808080')
  })
})
