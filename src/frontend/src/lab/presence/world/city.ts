import { rng } from '../body/shapes'

/**
 * The first world: a city at night with fires on the horizon. Drawn at the art's own size
 * (one canvas pixel is one art pixel) and shown enlarged without smoothing.
 */
export interface Building {
  x: number
  w: number
  h: number
  /** Lit windows: x, y in the building, colour index, flickers. */
  windows: { x: number; y: number; c: number; flicker: boolean }[]
  antenna: number
}

export interface City {
  w: number
  h: number
  horizon: number
  far: Building[]
  mid: Building[]
  near: Building[]
  tower: { x: number; w: number; h: number }
  plumes: { x: number; seed: number }[]
  sign: { x: number; y: number; h: number }
}

export const SKY = ['#07060d', '#100919', '#1f0c22', '#3c1024', '#6d1a22', '#a8331e', '#d8611f']
export const WINDOW_COLOURS = ['#ffb35a', '#5fd0ff', '#ff4f9a', '#ffe7b0']

function row(r: () => number, w: number, base: number, minW: number, maxW: number, minH: number, maxH: number, lit: number): Building[] {
  const out: Building[] = []
  let x = -Math.floor(r() * maxW)
  while (x < w) {
    const bw = minW + Math.floor(r() * (maxW - minW))
    const bh = Math.floor(base * (minH + r() * (maxH - minH)))
    const windows: Building['windows'] = []
    for (let wy = 3; wy < bh - 2; wy += 3)
      for (let wx = 2; wx < bw - 2; wx += 3)
        if (r() < lit) windows.push({ x: wx, y: wy, c: r() < 0.7 ? 0 : Math.floor(r() * WINDOW_COLOURS.length), flicker: r() < 0.06 })
    out.push({ x, w: bw, h: bh, windows, antenna: r() < 0.25 ? 2 + Math.floor(r() * 6) : 0 })
    x += bw + (r() < 0.3 ? 1 + Math.floor(r() * 3) : 0)
  }
  return out
}

export function makeCity(w: number, h: number, seed = 7): City {
  const r = rng(seed)
  const horizon = Math.round(h * 0.74)
  const far = row(r, w, horizon, 5, 16, 0.08, 0.3, 0.05)
  const mid = row(r, w, horizon, 10, 26, 0.12, 0.42, 0.16)
  const near = row(r, w, h * 0.9 - horizon + Math.round(h * 0.12), 14, 34, 0.55, 1, 0.1)
  const tw = Math.max(8, Math.round(w * 0.05))
  const tower = { x: Math.round(w * 0.16), w: tw, h: Math.round(horizon * 0.78) }
  const plumes = [0.07, 0.33, 0.62, 0.9].map((k) => ({ x: Math.round(w * k + (r() - 0.5) * w * 0.05), seed: r() }))
  const sign = { x: Math.round(w * 0.84), y: Math.round(horizon * 0.42), h: Math.max(10, Math.round(horizon * 0.2)) }
  return { w, h, horizon, far, mid, near, tower, plumes, sign }
}

const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5]

function hex(c: string): [number, number, number] {
  const n = parseInt(c.slice(1), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

/** The sky: bands of the palette blended with an ordered dither, never a smooth gradient. */
export function drawSky(g: CanvasRenderingContext2D, city: City) {
  const { w, horizon } = city
  const img = g.createImageData(w, horizon)
  const cols = SKY.map(hex)
  for (let y = 0; y < horizon; y++) {
    const f = (y / horizon) ** 1.7 * (cols.length - 1)
    const i = Math.floor(f)
    const k = f - i
    for (let x = 0; x < w; x++) {
      const t = (BAYER[(y % 4) * 4 + (x % 4)] + 0.5) / 16
      const c = cols[Math.min(cols.length - 1, k > t ? i + 1 : i)]
      const o = (y * w + x) * 4
      img.data[o] = c[0]
      img.data[o + 1] = c[1]
      img.data[o + 2] = c[2]
      img.data[o + 3] = 255
    }
  }
  g.putImageData(img, 0, 0)
}

function building(g: CanvasRenderingContext2D, b: Building, base: number, colour: string, time: number, seed: number) {
  g.fillStyle = colour
  g.fillRect(b.x, base - b.h, b.w, b.h)
  if (b.antenna) g.fillRect(b.x + Math.floor(b.w / 2), base - b.h - b.antenna, 1, b.antenna)
  b.windows.forEach((win, i) => {
    if (win.flicker && Math.sin(time * 7 + i * 13 + seed) > 0.3) return
    g.fillStyle = WINDOW_COLOURS[win.c]
    g.fillRect(b.x + win.x, base - b.h + win.y, 1, 1)
  })
}

/**
 * One frame of the city. The sky is drawn once by the caller; this adds everything that moves.
 * `time` is in seconds; `calm` stops the motion.
 */
export function drawCity(g: CanvasRenderingContext2D, city: City, sky: HTMLCanvasElement, time: number, calm: boolean) {
  const { w, h, horizon } = city
  const t = calm ? 0 : time
  g.drawImage(sky, 0, 0)

  // Fire on the horizon: a band that breathes.
  for (let i = 0; i < 4; i++) {
    const glow = 0.5 + 0.5 * Math.sin(t * (1.3 + i * 0.7) + i * 2)
    g.fillStyle = i % 2 ? '#ff8a2a' : '#ffb04a'
    for (let x = (i * 3) % 4; x < w; x += 4) {
      const lift = Math.round(2 + 3 * glow * (0.5 + 0.5 * Math.sin(x * 0.21 + i + t * 0.8)))
      g.fillRect(x, horizon - lift - i, 1, 1)
    }
  }

  // Smoke rising from the fires, in dithered puffs.
  g.fillStyle = '#2a1020'
  for (const p of city.plumes) {
    for (let k = 0; k < 16; k++) {
      const age = (t * 0.06 + k / 16 + p.seed) % 1
      const y = horizon - age * horizon * 0.9
      const x = p.x + Math.sin(age * 6 + p.seed * 9) * 6 + age * 18
      const rad = 2 + age * 9
      for (let dy = -rad; dy <= rad; dy++)
        for (let dx = -rad; dx <= rad; dx++) {
          if (dx * dx + dy * dy > rad * rad) continue
          const px = Math.round(x + dx)
          const py = Math.round(y + dy)
          if ((px + py) % 2 === 0 && py > 0) g.fillRect(px, py, 1, 1)
        }
    }
  }

  for (const b of city.far) building(g, b, horizon, '#1a0a19', t, 3)

  // The tower and its beacon.
  const tw = city.tower
  g.fillStyle = '#0f0611'
  g.fillRect(tw.x, horizon - tw.h, tw.w, tw.h)
  g.fillRect(tw.x + Math.floor(tw.w / 2) - 1, horizon - tw.h - 8, 2, 8)
  g.fillStyle = '#2a1430'
  for (let y = horizon - tw.h + 4; y < horizon; y += 6) g.fillRect(tw.x + 1, y, tw.w - 2, 1)
  if (Math.sin(t * 3) > 0) {
    g.fillStyle = '#ff2a3a'
    g.fillRect(tw.x + Math.floor(tw.w / 2) - 1, horizon - tw.h - 9, 2, 2)
  }

  for (const b of city.mid) building(g, b, horizon + 2, '#0c0510', t, 7)

  // The streets below the horizon: closer, darker blocks.
  const roofY = Math.round(h * 0.9)
  g.fillStyle = '#0a040d'
  g.fillRect(0, horizon + 2, w, roofY - horizon)
  for (const b of city.near) building(g, b, roofY, '#09030b', t, 11)

  // A neon sign that stutters.
  const s = city.sign
  const on = calm || Math.sin(t * 11) > -0.85 || Math.sin(t * 2.3) > 0.4
  g.fillStyle = '#12060f'
  g.fillRect(s.x - 1, s.y - 1, 6, s.h + 2)
  if (on) {
    g.fillStyle = '#ff3d8f'
    for (let y = 0; y < s.h; y += 4) {
      g.fillRect(s.x + 1, s.y + y, 3, 1)
      g.fillRect(s.x + 2, s.y + y + 1, 1, 2)
    }
  }

  // Embers drifting up.
  for (let i = 0; i < 46; i++) {
    const seed = (i * 0.618) % 1
    const age = (t * (0.05 + seed * 0.05) + seed) % 1
    const x = Math.round((seed * 997) % w + Math.sin(age * 9 + i) * 4)
    const y = Math.round(horizon - age * horizon * 0.8)
    g.fillStyle = age < 0.6 ? '#ffb04a' : '#a8331e'
    g.fillRect(x, y, 1, 1)
  }

  // Thin rain, slanted.
  g.fillStyle = '#2c2446'
  for (let i = 0; i < 38; i++) {
    const seed = (i * 0.754877) % 1
    const x = Math.round((seed * w + t * 30) % w)
    const y = Math.round((((i * 0.5698) % 1) * h + t * 90) % h)
    if (y < horizon) g.fillRect(x, y, 1, 2)
  }

  // The roof we stand on.
  const roof = Math.round(h * 0.9)
  g.fillStyle = '#060207'
  g.fillRect(0, roof, w, h - roof)
  g.fillRect(0, roof - 9, w, 1)
  for (let x = 2; x < w; x += 9) g.fillRect(x, roof - 9, 1, 9)
  g.fillStyle = '#1a0d1c'
  g.fillRect(0, roof, w, 1)
}
