import { describe, expect, it } from 'vitest'
import { weatherWords } from './weather'

describe('weatherWords', () => {
  it('says the town, the sky and a rounded temperature', () => {
    expect(weatherWords({ place: 'Gotham, United States', sky: 'Drizzle', temperature: 11.6, at: '' })).toBe('Gotham, United States: drizzling, 12 °C')
  })
})
