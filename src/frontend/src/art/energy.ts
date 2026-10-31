import { CHARACTER_COLORS, ENERGY_COLORS } from './palette'
import type { Pixel } from './sprite'

/**
 * How rested an agent is, from the room left in its conversation (1 = all of it):
 * fresh, then steady past 40% used, tired past 70%, exhausted past 90%.
 */
export type EnergyLevel = 'fresh' | 'steady' | 'tired' | 'exhausted'

export function energyLevel(left: number): EnergyLevel {
  if (left > 0.6) return 'fresh'
  if (left > 0.3) return 'steady'
  if (left > 0.1) return 'tired'
  return 'exhausted'
}

/** The battery stands left of the agent, above the desk: a cap, then a body of `cells` rows of charge. */
export const BATTERY = { x: 2, y: 10, width: 5, cells: 11 } as const

const CHARGE: Record<EnergyLevel, [keyof typeof ENERGY_COLORS, keyof typeof ENERGY_COLORS]> = {
  fresh: ['energy.high', 'energy.highShade'],
  steady: ['energy.mid', 'energy.midShade'],
  tired: ['energy.low', 'energy.lowShade'],
  exhausted: ['energy.low', 'energy.lowShade'],
}

/** Rows of charge for the energy left: at least one while any is left, so a nearly empty battery still shows red. */
export const chargeRows = (left: number) => (left <= 0 ? 0 : Math.max(1, Math.round(Math.min(1, left) * BATTERY.cells)))

/**
 * The battery for the energy left, in the station's own pixels. The charge fills from
 * the bottom, so its height reads the level without the colours; it glows a little in
 * the dark, like a light on a device.
 */
export function batteryPixels(left: number): Pixel[] {
  const { x, y, width, cells } = BATTERY
  const outline = CHARACTER_COLORS.outline
  const [base, shade] = CHARGE[energyLevel(left)]
  const filled = chargeRows(left)
  const pixels: Pixel[] = []
  for (let i = 1; i < width - 1; i++) pixels.push({ x: x + i, y, color: outline })
  const top = y + 1
  const bottom = top + cells + 1
  for (let row = top; row <= bottom; row++)
    for (let col = 0; col < width; col++) {
      const edge = row === top || row === bottom || col === 0 || col === width - 1
      if (edge) {
        pixels.push({ x: x + col, y: row, color: outline })
        continue
      }
      const lit = bottom - row <= filled
      const name = lit ? (col === width - 2 ? shade : base) : 'energy.empty'
      pixels.push({ x: x + col, y: row, color: ENERGY_COLORS[name], glow: true })
    }
  return pixels
}
