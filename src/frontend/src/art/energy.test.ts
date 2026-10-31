import { describe, expect, it } from 'vitest'
import { COFFEE, poseOf } from './agentStates'
import { BATTERY, batteryPixels, chargeRows, energyLevel } from './energy'
import { ENERGY_COLORS, SKIN_TONES } from './palette'
import { STATION, stationPixels } from './station'
import { THEMES } from './themes'

const look = { skin: 'fair', hair: 'brown', hairStyle: 'bun', cloth: 'blue' } as const
const charge = (left: number) => batteryPixels(left).filter((p) => p.color !== ENERGY_COLORS['energy.empty'] && p.glow)

describe('energy levels', () => {
  it('goes from fresh to exhausted as the conversation fills', () => {
    expect([1, 0.61, 0.6, 0.31, 0.3, 0.11, 0.1, 0].map(energyLevel)).toEqual([
      'fresh', 'fresh', 'steady', 'steady', 'tired', 'tired', 'exhausted', 'exhausted',
    ])
  })

  it('keeps one row of charge while any energy is left', () => {
    expect([0, 0.01, 0.5, 1, 1.2].map(chargeRows)).toEqual([0, 1, 6, BATTERY.cells, BATTERY.cells])
  })
})

describe('the battery', () => {
  it('charges higher with more energy, so the level reads without colours', () => {
    const top = (left: number) => Math.min(...charge(left).map((p) => p.y))
    expect(top(0.9)).toBeLessThan(top(0.5))
    expect(top(0.5)).toBeLessThan(top(0.15))
    expect(charge(0)).toEqual([])
  })

  it('is green when fresh, yellow halfway and red when tired', () => {
    const colors = (left: number) => new Set(charge(left).map((p) => p.color))
    expect(colors(0.9).has(ENERGY_COLORS['energy.high'])).toBe(true)
    expect(colors(0.45).has(ENERGY_COLORS['energy.mid'])).toBe(true)
    expect(colors(0.2).has(ENERGY_COLORS['energy.low'])).toBe(true)
  })

  it('stands left of the agent above the desk and below the room for signals, whatever the pose', () => {
    for (const state of ['working', 'waiting', 'done', 'failed'] as const)
      for (const left of [0, 0.5, 1]) {
        const pixels = stationPixels(look, poseOf(state, 0, undefined, { left }), THEMES.cozy)
        expect(pixels.every((p) => p.x >= 0 && p.x < STATION.width && p.y >= 0 && p.y < STATION.height)).toBe(true)
      }
    const battery = batteryPixels(1)
    expect(Math.max(...battery.map((p) => p.x))).toBeLessThan(8)
    expect(Math.max(...battery.map((p) => p.y))).toBeLessThan(STATION.deskY)
  })

  it('shows only when the energy is known', () => {
    const glowing = (left?: number) =>
      stationPixels(look, poseOf('working', 1, undefined, left === undefined ? undefined : { left }), THEMES.cozy).some((p) => p.x < 7 && p.glow)
    expect([glowing(0.5), glowing()]).toEqual([true, false])
  })
})

describe('a tired agent', () => {
  const faces = (left: number, state: 'working' | 'waiting' = 'working') =>
    new Set(Array.from({ length: 48 }, (_, t) => poseOf(state, t, undefined, { left }).emotion))

  it('works with heavy eyes and yawns now and then', () => {
    expect(faces(0.2)).toEqual(new Set(['tired', 'yawn', 'blink']))
    expect(faces(0.8).has('tired')).toBe(false)
  })

  it('sinks into its chair and types slower when exhausted', () => {
    const pose = poseOf('working', 9, undefined, { left: 0.05 })
    expect([pose.slump, pose.frame]).toEqual([2, 4])
    expect(poseOf('working', 9, undefined, { left: 0.2 }).slump).toBeUndefined()

    const head = (slump?: number) =>
      Math.min(...stationPixels(look, { emotion: 'tired', gesture: 'typing', frame: 0, screen: 'on', slump }, THEMES.cozy)
        .filter((p) => p.color === SKIN_TONES.fair[0] && p.y < STATION.deskY).map((p) => p.y))
    expect(head(2) - head()).toBe(2)
  })

  it('keeps the look of a state that asks for you', () => {
    expect(faces(0.05, 'waiting').has('tired')).toBe(false)
    expect(poseOf('waiting', 0, undefined, { left: 0.05 }).signal).toBe('question')
  })
})

describe('a coffee break', () => {
  it('sips while the battery charges, then smiles, then works as usual', () => {
    const at = (sinceRefresh: number) => poseOf('working', 2, undefined, { left: 1, sinceRefresh })
    expect([at(0).gesture, at(0).energy]).toEqual(['sip', 0])
    expect([at(COFFEE.sip / 2).gesture, at(COFFEE.sip / 2).energy]).toEqual(['sip', 0.5])
    expect([at(COFFEE.sip + 1).gesture, at(COFFEE.sip + 1).emotion]).toEqual(['typing', 'happy'])
    expect(at(COFFEE.smile + 1).emotion).toBe('focused')
  })

  it('draws a mug in front of the mouth', () => {
    const pixels = stationPixels(look, poseOf('working', 0, undefined, { left: 1, sinceRefresh: 1 }), THEMES.cozy)
    expect(pixels.filter((p) => p.color === '#d9574a').length).toBeGreaterThan(4)
    // The mouth sits on row 12 of the head, columns 11 and 12: the rim of the mug covers it.
    const mouth = (x: number) => pixels.findLast((p) => p.x === x && p.y === STATION.deskY - 14 + 12)?.color
    expect([mouth(8 + 12), mouth(8 + 13)]).toEqual(['#ece8e0', '#ece8e0'])
  })
})
