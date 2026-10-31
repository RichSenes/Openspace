import type { Sky } from '../api/types'
import type { Light } from './lighting'
import type { Pixel, Resolve } from './sprite'

/** A window in a back wall, 24 by 22: a frame, four panes split by a cross, and a sill. */
export const WINDOW_SIZE = { width: 24, height: 22 } as const

/** Where the panes are inside the frame: what they show is drawn here. */
export const GLASS = { x: 2, y: 2, width: 20, height: 16 } as const

/** The time of day the sky shows, from the hour on the viewer's clock. */
export type DayPhase = 'day' | 'dusk' | 'night'

export const phaseOf = (hour: number): DayPhase =>
  hour >= 8 && hour < 18 ? 'day' : (hour >= 6 && hour < 8) || (hour >= 18 && hour < 21) ? 'dusk' : 'night'


const SKIES: Record<DayPhase, { bands: string[]; skyline: string; cloud?: [string, string] }> = {
  day: { bands: ['#5aa8ec', '#7cbcf2', '#a6d4fa', '#c8e6ff'], skyline: '#7d93b4', cloud: ['#ffffff', '#dcebf7'] },
  dusk: { bands: ['#3b3a78', '#8a4f8e', '#d0708a', '#f4a35e'], skyline: '#33284a', cloud: ['#f6c7a8', '#d99484'] },
  night: { bands: ['#090d26', '#0e1433', '#141c44', '#1c2652'], skyline: '#05070f' },
}

/** The grey a bad sky turns towards, by time of day, and how far each weather takes it. */
const OVERCAST: Record<DayPhase, string> = { day: '#8a939e', dusk: '#675a6c', night: '#15181e' }
const STORMY: Record<DayPhase, string> = { day: '#525866', dusk: '#41384a', night: '#0c0e13' }
const FOG: Record<DayPhase, string> = { day: '#c3c8cf', dusk: '#a39aa3', night: '#3a3e47' }
const GREYING: Record<Sky, number> = { Clear: 0, Cloudy: 0.72, Fog: 0, Drizzle: 0.78, Rain: 0.85, Snow: 0.7, Storm: 0.9 }

/** Drops: how many columns rain, how long a drop is and how fast it falls, in pixels per frame. */
const DROPS: Partial<Record<Sky, { density: number; length: number; speed: number }>> = {
  Drizzle: { density: 0.3, length: 1, speed: 2 },
  Rain: { density: 0.5, length: 3, speed: 3 },
  Storm: { density: 0.65, length: 4, speed: 4 },
}

const RAIN: Record<DayPhase, string> = { day: '#e6eef7', dusk: '#d8c8dc', night: '#6f84a3' }
const SNOW = '#f4f7ff'
const FLASH = '#e3e8ff'

/** Frames between the chances of a lightning flash in a storm, at five frames a second. */
const STORM_EVERY = 37

/** A number from 0 to 1 that looks random but is always the same for the same n. */
const noise = (n: number) => (((Math.imul(n | 0, 2654435761) >>> 0) % 10007) / 10007)

const toRgb = (color: string) => [1, 3, 5].map((i) => parseInt(color.slice(i, i + 2), 16))
const blend = (a: string, b: string, t: number) => {
  const [x, y] = [toRgb(a), toRgb(b)]
  return '#' + x.map((v, i) => Math.round(v + (y[i] - v) * t).toString(16).padStart(2, '0')).join('')
}

/** A small cloud drifting by, `cx` columns into its loop: three rows, the bottom one in shade. */
const cloudRow = (cx: number, y: number, top: number) =>
  (y === top && cx >= 3 && cx <= 8) || (y === top + 1 && cx >= 0 && cx <= 12) || (y === top + 2 && cx >= 2 && cx <= 10)

/** Is a storm flashing in this frame: now and then, never two flashes in a row. */
export const flashing = (tick: number) => tick % STORM_EVERY < 2 && noise(Math.floor(tick / STORM_EVERY)) > 0.45

/**
 * The sky seen through a window, `width` by `height`, starting at world column `worldX` so
 * neighbouring windows show one continuous sky: bands of colour for the time of day, a
 * far skyline, clouds drifting by day and twinkling stars and lit windows by night. The
 * weather greys it, crowds it with clouds, veils it in fog and sends rain, snow or a storm
 * across it, in front of the skyline. It shines by itself, so the lights being off never dim it.
 */
export function skyPixels(worldX: number, width: number, height: number, tick: number, phase: DayPhase, weather: Sky = 'Clear'): Pixel[] {
  const sky = SKIES[phase]
  const grey = GREYING[weather]
  const toward = (weather === 'Storm' ? STORMY : OVERCAST)[phase]
  const flash = weather === 'Storm' && flashing(tick)
  const overcast = grey > 0
  const drops = DROPS[weather]
  const pixels: Pixel[] = []
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++) {
      const wx = worldX + x
      const block = Math.floor(wx / 5)
      const tower = 2 + Math.floor(noise(block) * 5)
      let color: string
      if (y >= height - tower) {
        // Far buildings; at night a few of their windows are lit.
        const lit = phase !== 'day' && wx % 2 === 0 && (height - y) % 2 === 0 && noise(wx * 7 + y) > 0.55
        color = lit ? '#ffd27a' : overcast ? blend(sky.skyline, toward, grey * 0.4) : sky.skyline
      } else {
        const t = (y / height) * (sky.bands.length - 1)
        const band = Math.floor(t)
        // Dithered steps between bands, as pixel art does instead of gradients.
        color = sky.bands[Math.min(sky.bands.length - 1, band + (t - band > 0.5 && (x + y) % 2 === 0 ? 1 : 0))]
        if (overcast) color = blend(color, toward, grey)
        if (weather === 'Clear' && sky.cloud) {
          if (cloudRow(((wx + Math.floor(tick / 3)) % 56) - 20, y, 3)) color = y === 5 ? sky.cloud[1] : sky.cloud[0]
        } else if (weather === 'Clear') {
          if (noise(wx * 31 + y * 17) > 0.97 && (tick + wx) % 12 < 9) color = '#e8ecff'
        } else if (overcast) {
          // Two layers of heavier clouds, the near one faster; dark at night too.
          const light = blend(toward, '#ffffff', phase === 'night' ? 0.1 : 0.42)
          const shade = blend(toward, '#000000', 0.2)
          const far = ((wx + Math.floor(tick / 5)) % 34) - 6
          const near = ((wx + 17 + Math.floor(tick / 2)) % 30) - 4
          if (cloudRow(far, y, 1)) color = y === 3 ? shade : light
          if (cloudRow(near, y, 5)) color = y === 7 ? shade : light
        }
        if (flash) color = blend(color, FLASH, 0.7)
      }
      if (weather === 'Fog') {
        // A veil over everything, thicker low down, with paler bands drifting through it.
        const thick = 0.55 + (y / height) * 0.3
        const streak = (y + Math.floor((wx + tick / 4) / 9)) % 5 === 0 ? 0.12 : 0
        color = blend(color, FOG[phase], Math.min(0.95, thick + streak))
      }
      if (drops) {
        // Each rainy column has its own drop, slanting a pixel left every three rows.
        const column = wx + Math.floor(y / 3)
        if (noise(column * 13) < drops.density) {
          const fall = (y + Math.floor(noise(column * 29) * 40) - tick * drops.speed) % 12
          if ((fall + 12) % 12 < drops.length) color = RAIN[phase]
        }
      } else if (weather === 'Snow') {
        // Flakes fall slowly and sway a pixel either way.
        const sway = Math.round(Math.sin((y + tick) / 3))
        const column = wx + sway
        if (noise(column * 17) < 0.3 && (y + Math.floor(noise(column * 23) * 24) - Math.floor(tick / 2) + 240) % 8 === 0) color = SNOW
      }
      pixels.push({ x, y, color, glow: true })
    }
  return pixels
}

/**
 * Light coming in through a window, in its own pixels: daylight shines into a dark room, the
 * night barely; a bad sky lets in less and greyer light, and a storm's flash lights it all up.
 */
export function windowLight(phase: DayPhase, weather: Sky = 'Clear', tick = 0): Light[] {
  if (weather === 'Storm' && flashing(tick)) return [{ x: 12, y: 22, radius: 40, color: FLASH }]
  if (phase === 'night') return [{ x: 12, y: 20, radius: 14, color: '#8fa6e8' }]
  const color = phase === 'day' ? '#fff4d6' : '#ffc89a'
  if (GREYING[weather] > 0 || weather === 'Fog') return [{ x: 12, y: 22, radius: 26, color: blend(color, '#c9d2dc', 0.6) }]
  return [{ x: 12, y: 22, radius: 34, color }]
}

const GLASS_TINT = [207, 230, 255]

/** A colour seen through glass: a little paler and bluer, so a window never reads as a hole. */
export function glazed(color: string): string {
  const rgb = [1, 3, 5].map((i) => parseInt(color.slice(i, i + 2), 16))
  return '#' + rgb.map((v, i) => Math.round(v + (GLASS_TINT[i] - v) * 0.22).toString(16).padStart(2, '0')).join('')
}

/**
 * A window: the view behind it, in glass coordinates, framed in the theme's trim. A faint
 * glint in the corner of each pane says it is glass.
 */
export function windowPixels(view: Pixel[], colors: Resolve): Pixel[] {
  const outline = colors('outline') ?? '#000000'
  const frame = colors('trim.light') ?? '#888888'
  const shade = colors('trim.dark') ?? '#555555'
  // By position, so the frame simply covers the view where they meet.
  const pixels = new Map<number, Pixel>()
  for (const p of view)
    if (p.x >= 0 && p.y >= 0 && p.x < GLASS.width && p.y < GLASS.height)
      pixels.set((p.y + GLASS.y) * 64 + p.x + GLASS.x, { ...p, color: glazed(p.color), x: p.x + GLASS.x, y: p.y + GLASS.y, glow: true })
  const set = (x: number, y: number, color: string) => pixels.set(y * 64 + x, { x, y, color })
  const { width } = WINDOW_SIZE
  const midX = GLASS.x + GLASS.width / 2 - 1
  const midY = GLASS.y + GLASS.height / 2
  for (let x = 0; x < width; x++) {
    set(x, 0, outline)
    set(x, 1, frame)
    set(x, 18, frame)
    set(x, 19, outline)
    set(x, 20, frame)
    set(x, 21, shade)
  }
  for (let y = 0; y < 20; y++) {
    set(0, y, outline)
    set(1, y, frame)
    set(width - 2, y, frame)
    set(width - 1, y, outline)
    set(midX, y, frame)
    set(midX + 1, y, shade)
  }
  for (let x = 1; x < width - 1; x++) set(x, midY, frame)
  for (const [px, py] of [[GLASS.x, GLASS.y], [midX + 2, GLASS.y], [GLASS.x, midY + 1], [midX + 2, midY + 1]]) {
    set(px + 1, py, '#ffffff')
    set(px, py + 1, '#ffffff')
  }
  return [...pixels.values()]
}

/** A doorway in a back wall, 32 by 32: a dark opening, or the view behind it, under a lintel and between two posts. */
export function doorwayPixels(view: Pixel[] | undefined, colors: Resolve): Pixel[] {
  const outline = colors('outline') ?? '#000000'
  const frame = colors('trim.light') ?? '#888888'
  const pixels: Pixel[] = []
  for (let y = 0; y < 32; y++)
    for (let x = 0; x < 32; x++) {
      const edge = x === 0 || x === 31 || y === 2
      const post = x <= 2 || x >= 29 || y <= 4
      if (y < 2) continue
      if (edge) pixels.push({ x, y, color: outline })
      else if (post) pixels.push({ x, y, color: frame })
    }
  const seen = new Map<number, Pixel>()
  for (const p of view ?? []) {
    const [x, y] = [p.x + 3, p.y + 5]
    if (x >= 3 && x < 29 && y >= 5 && y < 32) seen.set(y * 32 + x, { ...p, x, y, glow: true })
  }
  for (let y = 5; y < 32; y++) for (let x = 3; x < 29; x++) if (!seen.has(y * 32 + x)) pixels.push({ x, y, color: outline })
  return [...pixels, ...seen.values()]
}
