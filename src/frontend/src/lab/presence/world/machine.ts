import { rng } from '../body/shapes'
import { BITS, bayerAt, ditherBands, glyph, hash1 } from './pixels'
import type { Hour, Scene, Weather } from './types'

/**
 * Inside the machine: a circuit board with signals running along its traces, chips with their
 * lights, the processor's socket Shiori stands on and a data bus flowing beneath. The hour is the
 * machine's day (booting, busy, winding down, asleep); the weather is its mood (calm, heat,
 * static, a trickle or a rain of code, drifting bits, overload).
 */
export interface Trace {
  points: [number, number][]
  length: number
  seed: number
}

export interface Chip {
  x: number
  y: number
  w: number
  h: number
  led: number
}

export interface Machine {
  w: number
  h: number
  floor: number
  socket: { x: number; y: number; w: number; h: number }
  traces: Trace[]
  chips: Chip[]
  columns: number[]
}

const GRID = 3

export function makeMachine(w: number, h: number, seed = 61): Machine {
  const r = rng(seed)
  const floor = Math.round(h * 0.9)
  const socket = { x: Math.round(w * 0.32), y: Math.round(h * 0.8), w: Math.round(w * 0.36), h: floor - Math.round(h * 0.8) - 1 }
  const traces: Trace[] = []
  for (let i = 0; i < Math.round(w / 9); i++) {
    let x = Math.floor((r() * w) / GRID) * GRID
    let y = Math.floor((r() * floor) / GRID) * GRID
    const points: [number, number][] = [[x, y]]
    let horizontal = r() < 0.6
    let length = 0
    for (let k = 0; k < 6 + Math.floor(r() * 8); k++) {
      const step = (2 + Math.floor(r() * 10)) * GRID * (r() < 0.5 ? -1 : 1)
      const nx = horizontal ? Math.max(0, Math.min(w - 1, x + step)) : x
      const ny = horizontal ? y : Math.max(0, Math.min(floor - 2, y + step))
      length += Math.abs(nx - x) + Math.abs(ny - y)
      x = nx
      y = ny
      points.push([x, y])
      horizontal = !horizontal
    }
    traces.push({ points, length, seed: r() })
  }
  // Chips stay off the middle, where Shiori stands.
  const chips: Chip[] = []
  for (let i = 0; i < 40 && chips.length < 9; i++) {
    const cw = 14 + Math.floor(r() * 18)
    const ch = 10 + Math.floor(r() * 12)
    const x = Math.floor(r() * (w - cw))
    const y = Math.floor(r() * (floor * 0.85 - ch))
    const middle = x + cw > w * 0.3 && x < w * 0.7 && y < floor * 0.8
    const overlaps = chips.some((c) => x < c.x + c.w + 4 && x + cw + 4 > c.x && y < c.y + c.h + 4 && y + ch + 4 > c.y)
    if (!middle && !overlaps) chips.push({ x, y, w: cw, h: ch, led: r() })
  }
  const columns = Array.from({ length: Math.floor(w / 5) }, () => r())
  return { w, h, floor, socket, traces, chips, columns }
}

/** The point at distance d along a trace. */
export function along(t: Trace, d: number): [number, number] {
  let left = ((d % t.length) + t.length) % t.length
  for (let i = 1; i < t.points.length; i++) {
    const [ax, ay] = t.points[i - 1]
    const [bx, by] = t.points[i]
    const seg = Math.abs(bx - ax) + Math.abs(by - ay)
    if (left <= seg) {
      const k = seg ? left / seg : 0
      return [Math.round(ax + (bx - ax) * k), Math.round(ay + (by - ay) * k)]
    }
    left -= seg
  }
  return t.points[t.points.length - 1]
}

const GROUND: Record<Hour, string[]> = {
  night: ['#010403', '#020806', '#030c09', '#04110d'],
  dawn: ['#03060a', '#060c14', '#0a1420', '#0e1c28'],
  day: ['#02080a', '#041210', '#061a16', '#08221c'],
  dusk: ['#060408', '#0c0810', '#120c16', '#180f1c'],
}

/** How busy the machine is at an hour: signals, their speed, the share of lit chips. */
export const PACE: Record<Hour, { signals: number; speed: number; lit: number }> = {
  dawn: { signals: 1, speed: 30, lit: 0.6 },
  day: { signals: 3, speed: 55, lit: 1 },
  dusk: { signals: 1, speed: 20, lit: 0.5 },
  night: { signals: 0.4, speed: 12, lit: 0.2 },
}

function board(m: Machine, hour: Hour): HTMLCanvasElement {
  const c = document.createElement('canvas')
  c.width = m.w
  c.height = m.h
  const g = c.getContext('2d')!
  const img = g.createImageData(m.w, m.h)
  ditherBands(img, GROUND[hour], 1)
  g.putImageData(img, 0, 0)
  g.fillStyle = hour === 'day' ? '#0c2a22' : '#081c17'
  for (let y = 1; y < m.floor; y += 6) for (let x = 1; x < m.w; x += 6) g.fillRect(x, y, 1, 1)
  g.fillStyle = hour === 'day' ? '#145040' : '#0f3a30'
  for (const t of m.traces) {
    for (let i = 1; i < t.points.length; i++) {
      const [ax, ay] = t.points[i - 1]
      const [bx, by] = t.points[i]
      g.fillRect(Math.min(ax, bx), Math.min(ay, by), Math.abs(bx - ax) + 1, Math.abs(by - ay) + 1)
    }
    // Vias at the ends.
    for (const [x, y] of [t.points[0], t.points[t.points.length - 1]]) g.fillRect(x - 1, y - 1, 3, 3)
  }
  for (const ch of m.chips) {
    g.fillStyle = '#1f5a48'
    for (let x = ch.x + 2; x < ch.x + ch.w - 1; x += 2) {
      g.fillRect(x, ch.y - 1, 1, 1)
      g.fillRect(x, ch.y + ch.h, 1, 1)
    }
    g.fillStyle = '#040c0a'
    g.fillRect(ch.x, ch.y, ch.w, ch.h)
    g.fillStyle = '#163a30'
    g.fillRect(ch.x, ch.y, ch.w, 1)
    g.fillRect(ch.x, ch.y, 1, ch.h)
    g.fillStyle = '#0a1a16'
    g.fillRect(ch.x + 2, ch.y + 3, ch.w - 4, 1)
    g.fillRect(ch.x + 2, ch.y + 5, Math.round((ch.w - 4) * 0.6), 1)
  }
  // The socket: a frame of pins around a dark die.
  const s = m.socket
  g.fillStyle = '#0a1612'
  g.fillRect(s.x, s.y, s.w, s.h)
  g.fillStyle = '#2a6a58'
  for (let x = s.x + 1; x < s.x + s.w; x += 2) {
    g.fillRect(x, s.y, 1, 1)
    g.fillRect(x, s.y + s.h - 1, 1, 1)
  }
  return c
}

/** One frame of the machine over its board. */
export function drawMachine(g: CanvasRenderingContext2D, m: Machine, base: HTMLCanvasElement, time: number, calm: boolean, weather: Weather) {
  const t = calm ? 0 : time
  const sky = weather.sky
  const pace = PACE[weather.hour]
  const overload = sky === 'Storm'
  const alarm = overload && !calm && Math.sin(t * 6) > 0.5
  g.drawImage(base, 0, 0)

  // Booting: a sweep crosses the board and wakes the traces.
  if (weather.hour === 'dawn') {
    const sx = Math.round(((t % 6) / 6) * m.w)
    g.fillStyle = '#1e6a58'
    for (let y = 0; y < m.floor; y++) if (bayerAt(sx, y) < 0.7) g.fillRect(sx, y, 1, 1)
  }

  // Signals along the traces.
  m.traces.forEach((tr, i) => {
    const n = Math.max(0, Math.round(pace.signals * (0.5 + tr.seed)))
    for (let k = 0; k < n; k++) {
      const d = t * pace.speed * (0.6 + tr.seed) + (k / Math.max(1, n)) * tr.length + i * 13
      for (let tail = 0; tail < 5; tail++) {
        const [x, y] = along(tr, d - tail)
        g.fillStyle = alarm ? (tail === 0 ? '#ffd0d0' : '#ff3b3b') : tail === 0 ? '#d8fff0' : tail < 3 ? '#5dffc0' : '#1f8a6a'
        g.fillRect(x, y, 1, 1)
      }
    }
  })

  // Chip lights.
  for (const ch of m.chips) {
    const on = ch.led < pace.lit && Math.sin(t * (2 + ch.led * 9) + ch.led * 40) > -0.2
    g.fillStyle = on ? (alarm ? '#ff3b3b' : ch.led > 0.5 ? '#3cff9a' : '#ffb03a') : '#0a2018'
    g.fillRect(ch.x + ch.w - 3, ch.y + ch.h - 3, 1, 1)
  }

  // The die under Shiori breathes; faster when busy.
  const s = m.socket
  const breath = 0.5 + 0.5 * Math.sin(t * (weather.hour === 'night' ? 0.8 : 2.2))
  g.fillStyle = alarm ? '#ff5a3a' : '#3cff9a'
  for (let x = s.x + 3; x < s.x + s.w - 3; x++) if (bayerAt(x, s.y + 2) < 0.25 + 0.5 * breath) g.fillRect(x, s.y + 2 + ((x >> 3) % 2), 1, 1)

  // Rain of code.
  const share = sky === 'Rain' ? 0.5 : sky === 'Drizzle' ? 0.15 : overload ? 0.65 : 0
  if (share)
    m.columns.forEach((k, i) => {
      if (k > share) return
      const x = i * 5 + 1
      const speed = 25 + k * 60
      const head = ((t * speed + hash1(i) * m.h * 2) % (m.h * 1.6)) - m.h * 0.3
      for (let j = 0; j < 7; j++) {
        const y = Math.floor(head) - j * 6
        if (y < -5 || y > m.floor - 6) continue
        const bit = hash1(i * 31 + j + Math.floor(t * 4)) > 0.5 ? '1' : '0'
        g.fillStyle = j === 0 ? '#d8ffe8' : overload && i % 3 === 0 ? '#ff5a5a' : j < 3 ? '#3cff9a' : '#16603e'
        glyph(g, BITS[bit], x, y)
      }
    })

  // Drifting bits rise slowly.
  if (sky === 'Snow')
    for (let i = 0; i < Math.round(m.w / 10); i++) {
      const x = Math.round((hash1(i) * m.w + Math.sin(t * 0.5 + i) * 4) % m.w)
      const y = Math.round(m.floor - ((hash1(i + 9) * m.floor + t * (4 + hash1(i) * 5)) % m.floor))
      g.fillStyle = '#2a8a6a'
      glyph(g, BITS[i % 2 ? '1' : '0'], x, y)
    }

  // Static: a scatter of dead pixels that changes every frame.
  if (sky === 'Fog') {
    g.fillStyle = '#4a6a60'
    const n = Math.round(m.w * m.floor * 0.03)
    for (let i = 0; i < n; i++) g.fillRect(Math.floor(hash1(i + t * 97) * m.w), Math.floor(hash1(i * 7 + t * 53) * m.floor), 1, 1)
  }

  // Heat: the air above the board wavers.
  if ((sky === 'Cloudy' || overload) && !calm) {
    const c = g.canvas
    for (let y = 0; y < m.floor; y += 2) {
      const dx = Math.round(Math.sin(y * 0.15 + t * 4) * (overload ? 2 : 1))
      if (dx) g.drawImage(c, 0, y, m.w, 2, dx, y, m.w, 2)
    }
  }

  // Overload: glitch bands and sparks at the socket.
  if (overload && !calm) {
    const c = g.canvas
    for (let i = 0; i < 3; i++) {
      if (hash1(i + Math.floor(t * 10)) < 0.6) continue
      const y = Math.floor(hash1(i * 13 + Math.floor(t * 10)) * m.floor)
      g.drawImage(c, 0, y, m.w, 3, Math.round((hash1(i + t) - 0.5) * 16), y, m.w, 3)
    }
    g.fillStyle = '#ffe08a'
    for (let i = 0; i < 6; i++) {
      if (hash1(i + Math.floor(t * 8)) < 0.5) continue
      g.fillRect(s.x + Math.floor(hash1(i * 5 + Math.floor(t * 8)) * s.w), s.y - 1 - Math.floor(hash1(i * 3 + t) * 4), 1, 1)
    }
  }

  // The bus beneath: lanes of bits flowing to the right.
  g.fillStyle = '#020605'
  g.fillRect(0, m.floor, m.w, m.h - m.floor)
  g.fillStyle = '#0f3a30'
  g.fillRect(0, m.floor, m.w, 1)
  for (let lane = 0; lane < 3; lane++) {
    const y = m.floor + 2 + lane * 6
    if (y + 5 > m.h) break
    const speed = (pace.speed * (0.6 + lane * 0.3)) | 0
    for (let x = -((t * speed) % 8); x < m.w; x += 8) {
      const idx = Math.floor((x + t * speed) / 8) + lane * 101
      g.fillStyle = lane === 1 ? '#1f8a6a' : '#145040'
      glyph(g, BITS[hash1(idx) > 0.5 ? '1' : '0'], Math.round(x), y)
    }
  }
}

export function machineScene(w: number, h: number): Scene {
  const m = makeMachine(w, h)
  let base: HTMLCanvasElement | null = null
  let key = ''
  return {
    w,
    h,
    draw(g, time, calm, weather) {
      if (key !== weather.hour) {
        base = board(m, weather.hour)
        key = weather.hour
      }
      drawMachine(g, m, base!, time, calm, weather)
    },
  }
}
