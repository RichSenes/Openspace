import type { ShioriEvent, ShioriNow } from '../building/shiori'
import { CHAT_BUBBLE, CHAT_COLORS } from './chat'
import { unlit } from './lighting'
import { CHARACTER_COLORS } from './palette'
import { FACES, HEAD } from './sheets/character'
import { PROFILE_ARM, PROFILE_HEAD, PROFILE_LEGS } from './sheets/profile'
import { SHIORI_BACK, SHIORI_DRESS, SHIORI_EXPLAIN, SHIORI_GLYPHS, SHIORI_HAIR, SHIORI_SIDE_DRESS, SHIORI_SIDE_HAIR } from './sheets/shiori'
import { LEGS } from './sheets/standing'
import { pixelsOf, type Pixel, type SpriteDef } from './sprite'
import { WALKER } from './walker'

/**
 * Shiori's colours, taken from her portrait: warm skin, blue-black hair with a cool sheen,
 * a violet-black dress over dark tights, the red of her sash, gold pins, jade and blossom.
 * Her outline is darker than the agents' so the dark dress keeps its edge.
 */
export const SHIORI_COLORS: Record<string, string> = {
  ...CHARACTER_COLORS,
  outline: '#0e0a14',
  eye: '#1e120e',
  mouth: '#b8585e',
  blush: '#e89c90',
  'skin.light': '#ecc0a2',
  'skin.mid': '#d49c80',
  'skin.shadow': '#a86e5c',
  'hair.base': '#22263c',
  'hair.shade': '#161828',
  'hair.sheen': '#4a5480',
  'cloth.light': '#3a3058',
  'cloth.mid': '#2a2242',
  'cloth.dark': '#1a1428',
  'sash.light': '#c4303e',
  'sash.dark': '#7a1a2a',
  'pin.gold': '#e0b050',
  jade: '#4fb08a',
  blossom: '#e8a0b4',
  'blossom.heart': '#e0b050',
  'pants.light': '#241c36',
  'pants.dark': '#16111f',
  shoe: '#7a1a2a',
  'glyph.go': '#2f9e6a',
  'glyph.fail': '#d9574a',
  'glyph.ask': '#c98a1a',
  'glyph.done': '#2f9e6a',
}

/** The light of the projection that draws her: around her edge, at her feet, and the line that scans her. */
export const HOLOGRAM = { rim: '#7fe3ff', floor: '#4fb8d8', scan: '#c8f6ff' } as const

/** Her picture with its glow: one pixel of rim on each side and a little light under her feet. */
export const SHIORI_FRAME = { width: WALKER.width + 2, height: WALKER.height + 3, x: 1, y: 1 } as const

const colors = (name: string) => SHIORI_COLORS[name] ?? CHAT_COLORS[name] ?? null

/** Which icon her bubble shows for each piece of news. */
const GLYPH: Record<ShioriEvent, number> = { started: 0, failed: 1, waiting: 2, review: 3 }

/** Frames between two blinks, at five a second. */
const BLINK_EVERY = 22

/**
 * Shiori in her own 24 by 32 frame, as she is now: walking front, back or across, still
 * when she stands, explaining with her near hand when she talks at a desk, reading the
 * brain with her back to the room. She blinks now and then while facing the room.
 */
export function shioriPixels(now: Pick<ShioriNow, 'facing' | 'step' | 'mode'>, tick: number): Pixel[] {
  const layers: [SpriteDef, number][] = []
  if (now.facing === 'front') {
    const blink = tick % BLINK_EVERY === BLINK_EVERY - 1
    layers.push([LEGS, now.step], [SHIORI_DRESS, 0], [HEAD, 0], [blink ? FACES.blink : FACES.happy, 0], [SHIORI_HAIR, 0])
  } else if (now.facing === 'back') {
    layers.push([LEGS, now.step], [SHIORI_BACK, 0])
  } else {
    const arm: [SpriteDef, number] = now.mode === 'talking' ? [SHIORI_EXPLAIN, Math.floor(tick / 3) % 2] : [PROFILE_ARM, now.step]
    layers.push([PROFILE_LEGS, now.step], [SHIORI_SIDE_DRESS, 0], [PROFILE_HEAD, 0], [SHIORI_SIDE_HAIR, 0], arm)
  }
  const pixels = layers.flatMap(([sprite, frame]) => pixelsOf(sprite, frame, colors))
  return now.facing === 'left' ? pixels.map((p) => ({ ...p, x: WALKER.width - 1 - p.x })) : pixels
}

/** How she is projected: dimmed with the lights off (the night colour), and whether the scan line runs. */
export interface Projection {
  night?: string
  /** Only in the building, where she moves: still pictures never carry it. */
  scan?: boolean
}

/** The first row of her dress: the scan line never crosses her face. */
const BODY_TOP = 17

/**
 * The hologram around her pixels, in SHIORI_FRAME: the body (dimmed with the lights off,
 * and, when asked, a faint lighter line that runs down her dress now and then), and apart
 * from it the glow, meant to be drawn faint: a one pixel edge along her outline only (never
 * filling the gaps between her legs or arms) and a small pool at her feet.
 */
export function hologram(body: Pixel[], tick: number, projection: Projection = {}): { body: Pixel[]; glow: Pixel[] } {
  const { x: ox, y: oy } = SHIORI_FRAME
  const filled = new Set(body.map((p) => `${p.x},${p.y}`))
  const has = (x: number, y: number) => filled.has(`${x},${y}`)
  const scan = projection.scan ? BODY_TOP + (tick % SCAN_EVERY) : -1
  const shown = body.map((p) => {
    const base = projection.night ? dim(p.color, projection.night) : p.color
    return { x: p.x + ox, y: p.y + oy, color: p.y === scan ? tint(base, HOLOGRAM.scan, 0.2) : base }
  })
  const rim: Pixel[] = []
  const seen = new Set<string>()
  for (const p of body)
    for (const [dx, dy] of [
      [1, 0],
      [-1, 0],
      [0, 1],
      [0, -1],
    ]) {
      const [x, y] = [p.x + dx, p.y + dy]
      const k = `${x},${y}`
      if (has(x, y) || seen.has(k)) continue
      seen.add(k)
      // A gap of one or two pixels with her on both sides (between the feet, under an arm) is not her edge.
      const across = (has(x - 1, y) || has(x - 2, y)) && (has(x + 1, y) || has(x + 2, y))
      const down = (has(x, y - 1) || has(x, y - 2)) && (has(x, y + 1) || has(x, y + 2))
      if (across || down) continue
      rim.push({ x: x + ox, y: y + oy, color: HOLOGRAM.rim, glow: true })
    }
  const feet = WALKER.height + oy
  const pool: Pixel[] = []
  for (let x = 6; x < WALKER.width - 6; x++) pool.push({ x: x + ox, y: feet, color: HOLOGRAM.floor, glow: true })
  return { body: shown, glow: [...pool, ...rim.filter((p) => p.y < feet)] }
}

/** Frames between two passes of the line that scans her dress: about every twelve seconds. */
export const SCAN_EVERY = 60

/** How strongly the glow shows over what is behind her: faint, a little more in the dark. */
export const GLOW_ALPHA = { lit: 0.3, dark: 0.5 } as const

/** A colour moved part of the way towards another. */
export const tint = (a: string, b: string, t: number) =>
  '#' +
  [1, 3, 5]
    .map((i) => {
      const [x, y] = [parseInt(a.slice(i, i + 2), 16), parseInt(b.slice(i, i + 2), 16)]
      return Math.round(x + (y - x) * t).toString(16).padStart(2, '0')
    })
    .join('')

/** With the lights off she keeps some of her own light: halfway to the dark the room sinks to. */
const dim = (color: string, night: string) => tint(color, unlit(color, night), 0.5)

/** Her bubble while she talks: the round one of a chat, with the icon of her news inside. */
export const shioriBubble = (event: ShioriEvent): Pixel[] => [...pixelsOf(CHAT_BUBBLE, 0, colors), ...pixelsOf(SHIORI_GLYPHS, GLYPH[event], colors, 2, 2)]
