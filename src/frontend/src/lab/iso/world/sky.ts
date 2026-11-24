import { mix } from '../px'

/**
 * The light outside, by the hour and the weather. Inside, the lab is lit by cold lamps and does
 * not care what time it is; outside (the ground, the trees, the sky in the windows) follows the
 * sun: dawn, day, sunset, night. Bad weather greys it; a storm adds lightning, and now and then
 * the lab's own lights stutter.
 */

export type Weather = 'clear' | 'cloudy' | 'rain' | 'storm'

/** Light on the ground through the day: [hour, colour]. */
const GROUND: [number, string][] = [
  [0, '#28304e'],
  [5, '#323a5e'],
  [6.3, '#c99a94'],
  [7.5, '#f4e6d6'],
  [9, '#ffffff'],
  [16.5, '#fff6ea'],
  [18.5, '#f2a476'],
  [19.7, '#6a5a80'],
  [21, '#2c3352'],
  [24, '#28304e'],
]

/** The sky seen through the windows. */
const SKY: [number, string][] = [
  [0, '#0d1230'],
  [5, '#1a2048'],
  [6.3, '#e8987a'],
  [7.5, '#a8d4f2'],
  [12, '#7cc0f0'],
  [17, '#94c6ea'],
  [18.6, '#f08a5a'],
  [19.6, '#5a3c78'],
  [20.6, '#141a3c'],
  [24, '#0d1230'],
]

function along(keys: [number, string][], hour: number): string {
  const h = ((hour % 24) + 24) % 24
  for (let i = 0; i < keys.length - 1; i++) {
    const [h0, c0] = keys[i]
    const [h1, c1] = keys[i + 1]
    if (h >= h0 && h <= h1) return mix(c0, c1, (h - h0) / (h1 - h0 || 1))
  }
  return keys[0][1]
}

const GREY: Record<Weather, number> = { clear: 0, cloudy: 0.3, rain: 0.45, storm: 0.6 }

export function groundLight(hour: number, weather: Weather): string {
  const c = along(GROUND, hour)
  return mix(c, mix(c, '#5a6070', 0.5), GREY[weather])
}

export function skyColour(hour: number, weather: Weather): string {
  const c = along(SKY, hour)
  return mix(c, mix(c, '#6a7080', 0.6), GREY[weather])
}

/** Whether the street lamps are on. */
export function isDark(hour: number): boolean {
  const h = ((hour % 24) + 24) % 24
  return h < 6.6 || h > 19.2
}

/** In the small hours, very rarely, the city on the horizon burns. */
export function cityBurns(hour: number): boolean {
  const h = ((hour % 24) + 24) % 24
  return h >= 3 && h < 3.5
}

/** The hour now, with minutes, on this machine. */
export function hourNow(): number {
  const d = new Date()
  return d.getHours() + d.getMinutes() / 60
}

/**
 * Lightning and stuttering lights: given the time in seconds, how bright the flash is (0 to 1)
 * and whether the lab's lamps are down this instant. Both are rare and short.
 */
export function stormAt(t: number, weather: Weather): { flash: number; lampsDown: boolean } {
  if (weather !== 'storm' && weather !== 'rain') return { flash: 0, lampsDown: false }
  const slot = (period: number, salt: number) => {
    const k = Math.floor(t / period)
    const r = Math.abs(Math.sin(k * 12.9898 + salt) * 43758.5453) % 1
    return { into: t - k * period, r }
  }
  let flash = 0
  if (weather === 'storm') {
    const s = slot(9, 1)
    const at = s.r * 6
    const d = s.into - at
    if (d > 0 && d < 0.6) flash = d < 0.08 ? 1 : d > 0.18 && d < 0.26 ? 0.7 : 0
  }
  // The lamps stutter now and then: in a storm every so often, in the rain almost never.
  const l = slot(weather === 'storm' ? 23 : 71, 7)
  const at = l.r * 15
  const d = l.into - at
  const lampsDown = l.r > 0.35 && d > 0 && d < 0.5 && Math.floor(d * 14) % 3 !== 1
  return { flash, lampsDown }
}
