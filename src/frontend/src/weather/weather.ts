import type { Sky, WeatherNow } from '../api/types'

const WORDS: Record<Sky, string> = {
  Clear: 'clear',
  Cloudy: 'cloudy',
  Fog: 'foggy',
  Drizzle: 'drizzling',
  Rain: 'raining',
  Snow: 'snowing',
  Storm: 'stormy',
}

/** The weather in a few words, such as "Turin, Italy: raining, 12 °C". */
export const weatherWords = (now: WeatherNow) => `${now.place}: ${WORDS[now.sky]}, ${Math.round(now.temperature)} °C`
