import type { Sky } from '../../../api/types'

export type Hour = 'dawn' | 'day' | 'dusk' | 'night'
export const HOURS: Hour[] = ['dawn', 'day', 'dusk', 'night']
export const SKIES: Sky[] = ['Clear', 'Cloudy', 'Fog', 'Drizzle', 'Rain', 'Snow', 'Storm']

export interface Weather {
  hour: Hour
  sky: Sky
}

/** The hour of the world from the clock. */
export function hourOf(d: Date): Hour {
  const h = d.getHours() + d.getMinutes() / 60
  if (h >= 5.5 && h < 8) return 'dawn'
  if (h >= 8 && h < 17.5) return 'day'
  if (h >= 17.5 && h < 20.5) return 'dusk'
  return 'night'
}

/** A world drawn at its art size: it keeps whatever it caches and draws one frame at a time. */
export interface Scene {
  w: number
  h: number
  draw(g: CanvasRenderingContext2D, time: number, calm: boolean, weather: Weather): void
}

type V3 = [number, number, number]

export type WorldId = 'city' | 'tokyo' | 'space' | 'machine'

export interface WorldDef {
  id: WorldId
  label: string
  line: string
  /** Shiori's colours in pixel mode, darkest first (black stays black). */
  palette: V3[]
  /** What each of Openspace's skies becomes in this world. */
  skies: Record<Sky, string>
  hours: Record<Hour, string>
  create(w: number, h: number): Scene
}

export const EARTH_SKIES: Record<Sky, string> = { Clear: 'Clear', Cloudy: 'Cloudy', Fog: 'Fog', Drizzle: 'Drizzle', Rain: 'Rain', Snow: 'Snow', Storm: 'Storm' }
export const EARTH_HOURS: Record<Hour, string> = { dawn: 'Dawn', day: 'Day', dusk: 'Dusk', night: 'Night' }
