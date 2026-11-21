import { cityScene } from './city'
import { kyotoScene } from './kyoto'
import { machineScene } from './machine'
import { spaceScene } from './space'
import { EARTH_HOURS, EARTH_SKIES, type WorldDef, type WorldId } from './types'

/** The worlds Shiori can live in. Each keeps Openspace's hours and skies, in its own words. */
export const WORLDS: Record<WorldId, WorldDef> = {
  city: {
    id: 'city',
    label: 'Burning city',
    line: 'A city at night with fires on the horizon',
    palette: [
      [0, 0, 0],
      [0.1, 0.07, 0.22],
      [0.2, 0.19, 0.5],
      [0.31, 0.49, 1.0],
      [0.61, 0.42, 1.0],
      [1.0, 0.3, 0.6],
      [1.0, 0.62, 0.34],
      [0.95, 0.96, 1.0],
    ],
    skies: EARTH_SKIES,
    hours: EARTH_HOURS,
    create: (w, h) => cityScene('burning', w, h),
  },
  tokyo: {
    id: 'tokyo',
    label: 'Tokyo',
    line: 'Its tower, its sky tree, a train on the viaduct, signs on every block',
    palette: [
      [0, 0, 0],
      [0.07, 0.06, 0.2],
      [0.16, 0.2, 0.48],
      [0.25, 0.55, 1.0],
      [0.6, 0.4, 1.0],
      [1.0, 0.3, 0.65],
      [1.0, 0.82, 0.4],
      [0.95, 0.97, 1.0],
    ],
    skies: EARTH_SKIES,
    hours: EARTH_HOURS,
    create: (w, h) => cityScene('tokyo', w, h),
  },
  kyoto: {
    id: 'kyoto',
    label: 'Kyoto',
    line: 'A pagoda on the hill, maples, a vermilion gate and stone lanterns; the calm one',
    palette: [
      [0, 0, 0],
      [0.08, 0.06, 0.14],
      [0.22, 0.2, 0.36],
      [0.42, 0.5, 0.72],
      [0.85, 0.3, 0.2],
      [1.0, 0.55, 0.3],
      [1.0, 0.82, 0.55],
      [0.96, 0.95, 0.98],
    ],
    skies: EARTH_SKIES,
    hours: EARTH_HOURS,
    create: kyotoScene,
  },
  space: {
    id: 'space',
    label: 'Space',
    line: 'From a station: a ringed giant, a galaxy, a star that rises and sets',
    palette: [
      [0, 0, 0],
      [0.05, 0.07, 0.18],
      [0.12, 0.2, 0.45],
      [0.3, 0.5, 0.95],
      [0.55, 0.75, 1.0],
      [0.85, 0.55, 1.0],
      [1.0, 0.85, 0.6],
      [1.0, 1.0, 1.0],
    ],
    skies: { Clear: 'Clear', Cloudy: 'Nebula', Fog: 'Dust', Drizzle: 'Meteors', Rain: 'Meteor shower', Snow: 'Ice', Storm: 'Solar storm' },
    hours: { dawn: 'Sunrise', day: 'Daylight', dusk: 'Sunset', night: 'Night side' },
    create: spaceScene,
  },
  machine: {
    id: 'machine',
    label: 'Inside the machine',
    line: 'A circuit board alive with signals, and the core Shiori stands on',
    palette: [
      [0, 0, 0],
      [0.02, 0.1, 0.08],
      [0.05, 0.25, 0.2],
      [0.1, 0.55, 0.45],
      [0.3, 0.95, 0.7],
      [0.75, 1.0, 0.55],
      [1.0, 0.75, 0.3],
      [0.95, 1.0, 0.95],
    ],
    skies: { Clear: 'Calm', Cloudy: 'Heat', Fog: 'Static', Drizzle: 'Trickle', Rain: 'Code rain', Snow: 'Drifting bits', Storm: 'Overload' },
    hours: { dawn: 'Booting', day: 'Busy', dusk: 'Winding down', night: 'Asleep' },
    create: machineScene,
  },
}

export const WORLD_ORDER: WorldId[] = ['city', 'tokyo', 'kyoto', 'space', 'machine']
