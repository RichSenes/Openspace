import type { Pixel } from './sprite'

/** A source of light in a room: where it is, how far it reaches and its colour. */
export interface Light {
  x: number
  y: number
  radius: number
  color: string
}

const rgb = (hex: string) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16))
const hex = (c: number[]) => '#' + c.map((v) => Math.round(Math.min(255, Math.max(0, v))).toString(16).padStart(2, '0')).join('')
const mix = (a: number[], b: number[], t: number) => a.map((v, i) => v + (b[i] - v) * t)

/**
 * Brightness in rings around a light, so pools of light keep the hard steps of pixel
 * art instead of a smooth gradient.
 */
function ring(distance: number, radius: number): number {
  if (distance < radius * 0.35) return 0.85
  if (distance < radius * 0.7) return 0.6
  if (distance < radius) return 0.32
  return 0
}

/** A colour no light reaches, with the lights off. */
export const unlit = (color: string, night: string) => hex(mix(rgb(color), rgb(night), 0.72))

/**
 * The room with the lights off: everything sinks towards the night colour, except what
 * glows by itself, and each light brings back its surroundings in rings, tinted by its
 * colour.
 */
export function inTheDark(pixels: Pixel[], lights: Light[], night: string): Pixel[] {
  const dark = rgb(night)
  const sources = lights.map((l) => ({ ...l, rgb: rgb(l.color) }))
  const cache = new Map<string, string>()
  return pixels.map((p) => {
    if (p.glow) return p
    let best = 0
    let tint = dark
    for (const s of sources) {
      const level = ring(Math.hypot(p.x - s.x, p.y - s.y), s.radius)
      if (level > best) {
        best = level
        tint = s.rgb
      }
    }
    const key = `${p.color}${best}${tint.join()}`
    let color = cache.get(key)
    if (color === undefined) {
      const base = rgb(p.color)
      const unlit = mix(base, dark, 0.72)
      const lit = mix(base, base.map((v, i) => (v * tint[i]) / 255), 0.3)
      color = hex(mix(unlit, lit, best))
      cache.set(key, color)
    }
    return { ...p, color }
  })
}
