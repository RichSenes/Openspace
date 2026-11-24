import { iso } from './iso'
import { dot, glow } from './props'
import { mix, noise, Painter, rand } from './px'

/**
 * The lab's room: an open space of 18 × 18 tiles with two walls, its floor painted pixel by pixel
 * from what lies at each point (panels, the areas of the departments, the platform under Shiori,
 * the cables), and the live parts of the walls (screens, the city burning outside).
 */

export const N = 144
export const WALL = 96
export const OX = 300
export const OY = WALL + 12
export const SCENE_W = 600
export const SCENE_H = OY + 2 * N + 16
export const CENTER: [number, number] = [72, 72]
export const PLATFORM = 36

export function screenOf(a: number, b: number, z = 0): [number, number] {
  const [x, y] = iso(a, b, z)
  return [OX + x, OY + y]
}

export interface Area {
  name: string
  accent: string
  /** Rectangles in floor units, joined: areas are not boxes. */
  rects: [number, number, number, number][]
  floor: string
  pattern: 'grate' | 'raised' | 'plain' | 'carpet'
}

export const AREAS: Area[] = [
  { name: 'Research', accent: '#3fd29a', rects: [[0, 16, 46, 82], [0, 82, 30, 98]], floor: '#d3e2dd', pattern: 'grate' },
  { name: 'Engineering', accent: '#5b9bff', rects: [[12, 0, 128, 40], [100, 0, 144, 52]], floor: '#d2dbe8', pattern: 'raised' },
  { name: 'Data', accent: '#f2a948', rects: [[6, 98, 92, 144], [30, 88, 46, 98]], floor: '#e3ddd2', pattern: 'plain' },
  { name: 'Lounge', accent: '#c48bff', rects: [[100, 80, 144, 144]], floor: '#ddd7e6', pattern: 'carpet' },
]

/** Cables on the floor, as runs along the axes: thick, three side by side, from Shiori outwards. */
export const CABLES: [number, number][][] = [
  [
    [40, 60],
    [16, 60],
    [16, 30],
  ],
  [
    [62, 37],
    [62, 22],
    [50, 22],
    [50, 16],
  ],
  [
    [72, 107],
    [72, 112],
    [46, 112],
  ],
  [
    [96, 47],
    [96, 6],
  ],
]

function areaOf(a: number, b: number): { area: Area; edge: boolean } | null {
  for (const area of AREAS) {
    let hit = false
    let edge = false
    for (const [a0, b0, a1, b1] of area.rects) {
      if (a >= a0 && a < a1 && b >= b0 && b < b1) {
        hit = true
        // On the border only where no other rectangle of the same area continues.
        const near = (x: number, y: number) => area.rects.some(([c0, d0, c1, d1]) => x >= c0 && x < c1 && y >= d0 && y < d1)
        if (!near(a - 1.2, b) || !near(a + 1.2, b) || !near(a, b - 1.2) || !near(a, b + 1.2)) edge = true
      }
    }
    if (hit) return { area, edge }
  }
  return null
}

/** Where a point is on a cable: which of the three, and how far across it (0 to 1). */
function cableAt(a: number, b: number): { k: number; across: number; shadow: boolean } | null {
  for (const run of CABLES) {
    for (let i = 0; i < run.length - 1; i++) {
      const [a0, b0] = run[i]
      const [a1, b1] = run[i + 1]
      let along: number
      let off: number
      let lo: number
      let hi: number
      if (b0 === b1) {
        along = a
        off = b - b0 + 3
        lo = Math.min(a0, a1) - 3
        hi = Math.max(a0, a1) + 3
      } else {
        along = b
        off = a - a0 + 3
        lo = Math.min(b0, b1) - 3
        hi = Math.max(b0, b1) + 3
      }
      if (along < lo || along > hi) continue
      if (off >= 0 && off < 6) return { k: Math.floor(off / 2), across: (off % 2) / 2, shadow: false }
      if (off >= 6 && off < 7) return { k: 0, across: 0, shadow: true }
    }
  }
  return null
}

function floorColor(a: number, b: number): string {
  const ta = Math.floor(a / 8)
  const tb = Math.floor(b / 8)
  let c = (ta + tb) % 2 ? '#d7dee5' : '#d2d9e1'
  const found = areaOf(a, b)
  if (found) c = (ta + tb) % 2 ? found.area.floor : mix(found.area.floor, '#9aa6b4', 0.06)
  const fa = a % 8
  const fb = b % 8
  const seam = fa < 0.5 || fb < 0.5
  if (found) {
    const pattern = found.area.pattern
    if (pattern === 'raised' && ((Math.abs(fa - 2) < 0.6 && Math.abs(fb - 2) < 0.6) || (Math.abs(fa - 6) < 0.6 && Math.abs(fb - 6) < 0.6))) c = mix(c, '#4a5466', 0.35)
    if (pattern === 'grate' && ta % 3 === 1 && tb % 2 === 0 && fa > 1.5 && fa < 6.5 && fb > 1.5 && fb < 6.5 && Math.floor(fa * 2) % 2 === 0) c = mix(c, '#3a4352', 0.45)
    if (pattern === 'carpet') {
      const d = Math.hypot(a - 120, b - 116)
      if (d < 20) c = d > 18.5 ? mix(found.area.accent, '#ffffff', 0.3) : mix('#8a97a8', '#c48bff', 0.18 + 0.05 * noise(a / 3, b / 3))
    }
    if (found.edge) c = mix(found.area.accent, '#ffffff', 0.15)
  }
  if (seam && !(found?.area.pattern === 'carpet' && Math.hypot(a - 120, b - 116) < 20)) c = mix(c, '#7d8999', 0.22)
  // The platform under Shiori: rings and spokes, a lit edge.
  const d = Math.hypot(a - CENTER[0], b - CENTER[1])
  if (d < PLATFORM) {
    c = d > PLATFORM - 1.5 ? '#79e3f2' : d > PLATFORM - 3 ? '#aab6c3' : (Math.floor(d / 6) % 2 ? '#c6cfd9' : '#cdd5de')
    const ang = Math.atan2(b - CENTER[1], a - CENTER[0])
    if (d < PLATFORM - 3 && Math.abs(((ang / (Math.PI / 6)) % 1 + 1) % 1 - 0.5) > 0.47) c = '#b3bdc9'
  }
  const cable = cableAt(a, b)
  if (cable) {
    if (cable.shadow) c = mix(c, '#0b1220', 0.3)
    else {
      const body = ['#323a48', '#27405e', '#3d3446'][cable.k]
      c = cable.across < 0.25 ? mix(body, '#ffffff', 0.25) : cable.across > 0.75 ? mix(body, '#000000', 0.3) : body
    }
  }
  return c
}

/** The burning city through the window, in columns of the wall. */
function cityColumn(u: number, v: number, seed: number): string {
  const r = rand(Math.floor(u / 5) + seed)
  const h = 10 + Math.floor(r() * 34) + (Math.floor(u / 5) % 4 === 0 ? 10 : 0)
  if (v < h) {
    const lit = Math.floor(u / 2) % 2 === 0 && v % 4 === 1 && noise(u, v, 3) > 0.62
    return lit ? '#ffcf7a' : mix('#170c18', '#3b1a1e', v / 60)
  }
  // The sky: red smoke low, dark high.
  return mix('#ff7a3a', '#1c0f22', Math.min(1, (v - 8) / 54))
}

export interface Room {
  canvas: HTMLCanvasElement
  /** Paints what moves on the walls and floor: screens, fire light, cable pulses, lights. */
  live(ctx: CanvasRenderingContext2D, t: number): void
  /** The walls' own lights, for a dark room: screens, the window, the light strips. */
  glows(ctx: CanvasRenderingContext2D, t: number): void
}

export function makeRoom(): Room {
  const p = new Painter(SCENE_W, SCENE_H)
  // The walls first. The left one (a = 0) looks +a, the right one (b = 0) looks +b.
  const wallL = (b: number, z: number) => screenOf(0, b, z)
  const wallR = (a: number, z: number) => screenOf(a, 0, z)
  for (let z = 0; z < WALL; z++) {
    for (let col = 0; col < 2 * N; col++) {
      const b = col / 2
      const [x, y] = wallL(b, z)
      let c = z < 5 ? '#4a5466' : z === 5 ? '#62d6e8' : Math.floor(b) % 16 === 0 ? '#aab4c0' : z % 32 === 0 ? '#b3bdc8' : '#c4cdd7'
      if (z > WALL - 6) c = z === WALL - 4 ? '#e9fbff' : '#9fb0c0'
      // A dark glass board with marker on it.
      if (b >= 84 && b < 124 && z >= 30 && z < 72) {
        const u = Math.round(col - 168)
        const v = z - 30
        c = v < 2 || v > 39 || u < 2 || u > 77 ? '#5d6878' : '#1a2a33'
        const r = rand(u * 97 + v)
        if (c === '#1a2a33' && v % 6 < 2 && noise(u / 3, v, 9) > 0.55 && r() > 0.3) c = v % 12 < 6 ? '#7ff0ff' : '#ff9fd0'
      }
      // Shelves with jars near the corner.
      if (b >= 4 && b < 14 && z >= 40 && z < 76 && z % 12 < 2) c = '#8d99a8'
      if (b >= 4 && b < 14 && z >= 40 && z < 74 && z % 12 >= 2 && z % 12 < 8 && Math.floor(col) % 4 < 2) c = ['#4fd1a0', '#5aa6ff', '#ff8aa0', '#ffd36a'][Math.floor(col / 4 + z / 12) % 4]
      p.set(x, y, c)
      p.set(x - 1, y, c)
    }
  }
  for (let z = 0; z < WALL; z++) {
    for (let col = 0; col < 2 * N; col++) {
      const a = col / 2
      const [x, y] = wallR(a, z)
      let c = z < 5 ? '#4a5466' : z === 5 ? '#62d6e8' : Math.floor(a) % 16 === 0 ? '#bcc6d1' : z % 32 === 0 ? '#c5ced8' : '#d8dfe7'
      if (z > WALL - 6) c = z === WALL - 4 ? '#e9fbff' : '#aebccb'
      // A wide window onto the burning city.
      if (a >= 102 && a < 140 && z >= 22 && z < 84) {
        const u = col - 204
        const v = z - 22
        c = u < 2 || u > 73 || v < 2 || v > 59 ? '#6b7787' : Math.floor(u) % 25 === 0 ? '#6b7787' : cityColumn(u, v, 31)
      }
      // The frames of the monitoring screens (their content is live).
      if (a >= 48 && a < 96 && z >= 32 && z < 82) c = '#2a3140'
      p.set(x, y, c)
      p.set(x + 1, y, c)
    }
  }
  // The floor, pixel by pixel, from what is at each point.
  for (let y = OY; y < OY + 2 * N; y++) {
    for (let x = 0; x < SCENE_W; x++) {
      const dx = x + 0.5 - OX
      const dy = y + 0.5 - OY
      const a = (dx / 2 + dy) / 2
      const b = (dy - dx / 2) / 2
      if (a < 0 || b < 0 || a >= N || b >= N) continue
      p.set(x, y, floorColor(a, b))
    }
  }
  const canvas = p.canvas()

  // The live screens on the right wall, painted at ten frames a second.
  const screens = new Painter(SCENE_W, SCENE_H)
  const screenCanvas = document.createElement('canvas')
  screenCanvas.width = SCENE_W
  screenCanvas.height = SCENE_H
  let last = -1
  const history = Array.from({ length: 40 }, (_, i) => 0.5 + 0.3 * Math.sin(i * 0.4))

  function paintScreens(t: number) {
    screens.data.fill(0)
    const put = (a0: number, u: number, v: number, c: string) => {
      const [x, y] = screenOf(a0, 0, 0)
      screens.set(x + u, y + Math.floor(u / 2) - v, c)
    }
    const panel = (a0: number, wide: number, z0: number, high: number, fill: (u: number, v: number) => string) => {
      for (let u = 0; u < wide; u++) for (let v = 0; v < high; v++) put(a0, u, z0 + v, fill(u, v))
    }
    history.push(Math.max(0.1, Math.min(0.95, history[history.length - 1] + (Math.random() - 0.5) * 0.18)))
    history.shift()
    // A line chart of the last readings.
    panel(49, 44, 54, 26, (u, v) => {
      const val = history[Math.min(history.length - 1, Math.floor(u * (history.length / 44)))] * 22
      if (Math.abs(v - val) < 1) return '#7ff0ff'
      if (v < val) return mix('#0d2433', '#1d5a6e', v / 26)
      return u % 11 === 0 || v % 6 === 0 ? '#12303f' : '#0b1d29'
    })
    // Bars that rise and fall.
    panel(72, 22, 54, 26, (u, v) => {
      const bar = Math.floor(u / 4)
      const h = 4 + Math.round((Math.sin(t * 1.5 + bar * 1.3) * 0.5 + 0.5) * 18)
      if (u % 4 !== 3 && v < h) return v > h - 2 ? '#ffe08a' : '#f2a948'
      return '#0b1d29'
    })
    panel(84, 22, 54, 26, (u, v) => {
      // A ring that fills: how busy she is.
      const d = Math.hypot(u - 11, (v - 13) * 1)
      const ang = (Math.atan2(v - 13, u - 11) + Math.PI) / (Math.PI * 2)
      if (d > 7 && d < 10) return ang < (Math.sin(t * 0.4) * 0.5 + 0.5) ? '#c48bff' : '#2a2140'
      if (d < 3) return '#e9d6ff'
      return '#0b1d29'
    })
    // A heartbeat across the bottom.
    panel(49, 94, 35, 16, (u, v) => {
      const x = (u + t * 30) % 40
      const beat = x > 18 && x < 24 ? [0, 6, -4, 10, 2, 0][Math.floor(x - 18)] : 0
      if (Math.abs(v - 7 - beat) < 1) return '#5dff9a'
      return u % 10 === 0 ? '#0f2a22' : '#081a14'
    })
    const ctx = screenCanvas.getContext('2d')!
    const image = ctx.createImageData(SCENE_W, SCENE_H)
    image.data.set(screens.data)
    ctx.putImageData(image, 0, 0)
  }

  const embers = Array.from({ length: 24 }, (_, k) => ({ u: (k * 37) % 72, ph: (k * 0.137) % 1, speed: 0.08 + ((k * 7) % 10) / 80 }))

  return {
    canvas,
    glows(ctx, t) {
      for (const a of [60, 78, 90]) {
        const [x, y] = screenOf(a, 0, 60)
        glow(ctx, x, y, 46, '#5fc8ff', 0.4)
      }
      const [wx, wy] = screenOf(121, 0, 50)
      glow(ctx, wx, wy, 110, '#ff6a2a', 0.45 + 0.1 * Math.sin(t * 2.3))
      const [bx, by] = screenOf(0, 104, 50)
      glow(ctx, bx, by, 60, '#7ff0ff', 0.25)
      for (let k = 0; k <= 8; k++) {
        const [x1, y1] = screenOf(k * 18, 0, WALL - 4)
        const [x2, y2] = screenOf(0, k * 18, WALL - 4)
        glow(ctx, x1, y1 + 6, 30, '#cdeeff', 0.25)
        glow(ctx, x2, y2 + 6, 30, '#cdeeff', 0.25)
      }
    },
    live(ctx, t) {
      const tick = Math.floor(t * 10)
      if (tick !== last) {
        last = tick
        paintScreens(t)
      }
      ctx.drawImage(screenCanvas, 0, 0)
      // The fires outside breathe; embers rise past the window.
      const [wx, wy] = screenOf(121, 0, 40)
      glow(ctx, wx, wy, 70, '#ff6a2a', 0.18 + 0.08 * Math.sin(t * 2.3) + 0.05 * Math.sin(t * 7.1))
      for (const e of embers) {
        const ph = (t * e.speed + e.ph) % 1
        const [x, y] = screenOf(103 + e.u / 2, 0, 26 + ph * 54)
        dot(ctx, x, y, ph < 0.5 ? '#ffb15a' : '#ff6a3a', 1 - ph)
      }
      // Data running along the cables, towards her.
      for (const run of CABLES) {
        const len = run.slice(1).reduce((s, q, i) => s + Math.abs(q[0] - run[i][0]) + Math.abs(q[1] - run[i][1]), 0)
        for (let k = 0; k < 2; k++) {
          let s = (1 - ((t * 0.35 + k / 2 + run[0][0] / 50) % 1)) * len
          for (let i = 0; i < run.length - 1; i++) {
            const seg = Math.abs(run[i + 1][0] - run[i][0]) + Math.abs(run[i + 1][1] - run[i][1])
            if (s <= seg) {
              const f = s / seg
              const a = run[i][0] + (run[i + 1][0] - run[i][0]) * f
              const b = run[i][1] + (run[i + 1][1] - run[i][1]) * f
              const [x, y] = screenOf(a, b)
              dot(ctx, x - 1, y, '#9ff3ff', 1, 2)
              glow(ctx, x, y, 6, '#5fd7ff', 0.5)
              break
            }
            s -= seg
          }
        }
      }
      // The light strips on top of the walls.
      const [lx, ly] = screenOf(0, 0, WALL - 4)
      glow(ctx, lx, ly + 10, 120, '#cdeeff', 0.07)
    },
  }
}
