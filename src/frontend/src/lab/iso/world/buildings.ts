import { box, iso, onLeftFace, onRightFace, shades, YF, type Shades } from '../iso'
import type { Prop } from '../props'
import { mix, noise, Painter, rand } from '../px'

/**
 * The city's buildings, of several kinds so no two streets look alike: offices banded with
 * windows, glass towers, homes with balconies, shops with awnings, stepped blocks with a
 * terrace, low warehouses. Roofs differ too: gravel with machines on it, solar panels, now and
 * then a terrace with planters. A storey is 22 pixels; the street floor is taller.
 */

export type Kind = 'office' | 'glass' | 'home' | 'shop' | 'stepped' | 'warehouse'

const STREET = 40
const STOREY = 22

type At = (a: number, b: number, z?: number) => [number, number]

function canvasFor(da: number, db: number, height: number) {
  const extra = 10
  const w = 2 * (da + db) + 2 * extra
  const top = height + 30
  const h = Math.ceil((da + db) * YF) + top + 2 * extra
  const ax = 2 * db + extra
  const ay = top + extra
  const p = new Painter(w, h)
  const at: At = (a, b, z = 0) => {
    const [x, y] = iso(a, b, z)
    return [ax + x, ay + y]
  }
  return { p, at, ax, ay }
}

/** Paints a pixel of a face, as a window: dark glass, lit at night if `lit`. */
function windowPixel(p: Painter, face: 'left' | 'right', at: At, a: number, b: number, z: number, u: number, v: number, glass: string, lit: string | null) {
  const put = (c: string, alpha = 1) => (face === 'left' ? onLeftFace(p, at, a, b, z, u, v, c, alpha) : onRightFace(p, at, a, b, z, u, v, c, alpha))
  if (lit) {
    p.emitting = true
    put(lit, 0.85)
    p.emitting = false
  }
  put(glass)
}

/** Gravel, machines and a water tank; or solar panels; or a terrace with planters. */
function roof(p: Painter, at: At, a0: number, b0: number, da: number, db: number, z: number, r: () => number) {
  const kind = r()
  if (kind < 0.25) {
    for (let a = a0 + 2; a < a0 + da - 5; a += 6)
      for (let b = b0 + 2; b < b0 + db - 4; b += 5)
        p.poly([at(a, b, z + 1), at(a + 5, b, z + 1), at(a + 5, b + 4, z + 3), at(a, b + 4, z + 3)], (x, y) => ((x * 3 + y) % 4 === 0 ? '#6a8cc4' : '#2c4470'))
    return
  }
  // Gravel.
  p.poly([at(a0, b0, z), at(a0 + da, b0, z), at(a0 + da, b0 + db, z), at(a0, b0 + db, z)], (x, y) => (noise(x / 1.5, y / 1.5, 7) > 0.6 ? '#a8a49a' : noise(x, y, 9) > 0.7 ? '#8e8a80' : '#9c988e'))
  if (kind < 0.4 && da > 14 && db > 14) {
    // A terrace: planters along the edge, a table.
    for (let a = a0 + 2; a < a0 + da - 3; a += 5) box(p, at, a, b0 + db - 3, z, 3, 2, 3, shades('#b9b3a7'))
    for (let a = a0 + 2; a < a0 + da - 3; a += 5) {
      const [x, y] = at(a + 1.5, b0 + db - 2, z + 3)
      p.ellipse(x, y - 2, 3, 2.5, (_px, py) => (py < y - 2.5 ? '#79ae64' : '#467f4a'))
    }
    return
  }
  // Machines: a box with a fan, vents, a water tank.
  const n = 1 + Math.floor(r() * 3)
  for (let k = 0; k < n; k++) {
    const a = a0 + 2 + r() * Math.max(1, da - 10)
    const b = b0 + 2 + r() * Math.max(1, db - 9)
    box(p, at, a, b, z, 6, 5, 5, shades('#c3c8cc'))
    const [x, y] = at(a + 3, b + 2.5, z + 5)
    p.ellipse(x, y, 3, 1.6, '#5d6878')
  }
  if (r() > 0.5 && da > 12) {
    const [x, y] = at(a0 + da - 5, b0 + 4, z)
    p.rect(x - 1, y - 9, 1, 9, '#5d6878')
    p.rect(x + 4, y - 9, 1, 9, '#5d6878')
    p.ellipse(x + 2, y - 14, 5, 4, (px) => (px < x + 1 ? '#c9b48a' : '#a8946c'))
  }
}

export function building(kind: Kind, da: number, db: number, storeys: number, seed: number): Prop {
  const r = rand(seed)
  const height = STREET + storeys * STOREY + (kind === 'shop' || kind === 'warehouse' ? -12 : 0)
  const { p, at, ax, ay } = canvasFor(da, db, height)
  p.poly([at(0, 0), at(da + 4, 0), at(da + 4, db + 4), at(0, db + 4)], '#0b1220', 0.2)
  const night = (u: number, v: number, face: number) => (noise(u / 5 + face * 40, Math.floor(v / STOREY) * 3.1, seed) > 0.52 ? (r() > 0.35 ? '#ffdf9e' : '#c4e6ff') : null)

  if (kind === 'warehouse') {
    const c = ['#b9b0a0', '#9fa7ad', '#c4b8a2'][Math.floor(r() * 3)]
    box(p, at, 0, 0, 0, da, db, height, shades(c))
    // Ribbed walls, roll-up doors, a band of high windows.
    for (let u = 0; u < 2 * da; u += 3) for (let v = 0; v < height; v++) onLeftFace(p, at, 0, db, 0, u, v, mix(c, '#000000', 0.12))
    for (let d = 6; d < 2 * da - 14; d += 22) for (let u = d; u < d + 12; u++) for (let v = 0; v < 16; v++) onLeftFace(p, at, 0, db, 0, u, v, v % 2 ? '#7e858c' : '#8e959c')
    for (let u = 2; u < 2 * db - 2; u++) if (u % 5) windowPixel(p, 'right', at, da, 0, 0, u, height - 6, '#4b6378', night(u, 30, 1))
    roof(p, at, 0, 0, da, db, height, r)
    p.outline('#18202b')
    return { da, db, sprite: { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined } }
  }

  if (kind === 'shop') {
    const c = ['#e9dfcf', '#f2e8e0', '#dfe6e9', '#e8d6c2'][Math.floor(r() * 4)]
    box(p, at, 0, 0, 0, da, db, height, shades(c))
    // A shop front, an awning in stripes, a sign band of colour.
    const awning = ['#c2443e', '#2f7a6a', '#3a5f9a', '#e0a23a'][Math.floor(r() * 4)]
    for (let u = 3; u < 2 * da - 3; u++)
      for (let v = 3; v < 18; v++) {
        if (u % 16 < 2) continue
        windowPixel(p, 'left', at, 0, db, 0, u, v, '#3c5266', v > 6 ? '#ffe6b0' : null)
      }
    for (let u = 0; u < 2 * da; u++) for (let v = 19; v < 23; v++) onLeftFace(p, at, 0, db + (v - 19) * 0.6, 0, u, v, Math.floor(u / 4) % 2 ? awning : '#f4f0e8')
    p.emitting = true
    for (let u = 6; u < Math.min(2 * da - 6, 40); u++) for (let v = 24; v < 27; v++) onLeftFace(p, at, 0, db, 0, u, v, awning)
    p.emitting = false
    for (let u = 2; u < 2 * db - 2; u++) for (let v = 5; v < 16; v++) if (u % 10 > 1) windowPixel(p, 'right', at, da, 0, 0, u, v, '#3c5266', night(u, v, 1))
    roof(p, at, 0, 0, da, db, height, r)
    p.outline('#18202b')
    return { da, db, sprite: { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined } }
  }

  // The street floor: shop fronts or a lobby, the same for every other kind.
  const plinth = ['#3a4656', '#4a4338', '#2f3d3a'][Math.floor(r() * 3)]
  box(p, at, 0, 0, 0, da, db, STREET, { top: '#cfd8df', left: plinth, right: mix(plinth, '#000000', 0.2) })
  for (let u = 3; u < 2 * da - 3; u++) for (let v = 4; v < STREET - 8; v++) if (u % 18 > 1) windowPixel(p, 'left', at, 0, db, 0, u, v, '#2c3b4a', v > 10 ? '#ffe2a8' : null)
  for (let u = 3; u < 2 * db - 3; u++) for (let v = 4; v < STREET - 8; v++) if (u % 18 > 1) windowPixel(p, 'right', at, da, 0, 0, u, v, '#243241', null)

  let a0 = 0
  let b0 = 0
  let tda = da
  let tdb = db
  let floors = storeys
  if (kind === 'glass' || kind === 'stepped') {
    a0 = 2
    b0 = 2
    tda = da - 4
    tdb = db - 4
  }
  const z0 = STREET
  const top = (z: number, a: number, b: number, w: number, d: number) => roof(p, at, a, b, w, d, z, r)

  if (kind === 'stepped' && storeys >= 3) {
    // A lower, wide part with a terrace on it, then a narrower tower set back.
    const low = Math.ceil(storeys / 2)
    floors = storeys - low
    facade(p, at, a0, b0, tda, tdb, z0, low, 'office', r, night)
    const tz = z0 + low * STOREY
    // The terrace: decking and planters.
    p.poly([at(a0, b0, tz), at(a0 + tda, b0, tz), at(a0 + tda, b0 + tdb, tz), at(a0, b0 + tdb, tz)], (x, y) => ((x + y * 2) % 6 === 0 ? '#a07c56' : '#b88f64'))
    for (let a = a0 + 2; a < a0 + tda - 3; a += 5) {
      box(p, at, a, b0 + tdb - 3, tz, 3, 2, 3, shades('#cfc8bb'))
      const [x, y] = at(a + 1.5, b0 + tdb - 2, tz + 3)
      p.ellipse(x, y - 2, 3, 2.5, (_px, py) => (py < y - 2.5 ? '#79ae64' : '#467f4a'))
    }
    const sa = a0 + 2
    const sb = b0 + 2
    facade(p, at, sa, sb, Math.max(8, tda - 10), Math.max(8, tdb - 10), tz, floors, 'glass', r, night)
    top(tz + floors * STOREY, sa, sb, Math.max(8, tda - 10), Math.max(8, tdb - 10))
  } else {
    facade(p, at, a0, b0, tda, tdb, z0, floors, kind === 'stepped' ? 'office' : kind, r, night)
    top(z0 + floors * STOREY, a0, b0, tda, tdb)
  }
  p.outline('#18202b')
  return { da, db, sprite: { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined } }
}

/** The storeys above the street: banded office windows, a glass curtain wall, or homes with balconies. */
function facade(
  p: Painter,
  at: At,
  a0: number,
  b0: number,
  da: number,
  db: number,
  z0: number,
  floors: number,
  kind: 'office' | 'glass' | 'home',
  r: () => number,
  night: (u: number, v: number, face: number) => string | null,
) {
  const h = floors * 22
  if (h <= 0) return
  let colour: string
  let tone: Shades
  if (kind === 'glass') {
    colour = ['#5a8a9a', '#4f6f8f', '#7a7058', '#6a8f86'][Math.floor(r() * 4)]
    tone = shades(colour)
  } else if (kind === 'home') {
    colour = ['#d9a07a', '#e6c79a', '#e9ddc8', '#c98a76', '#d7c3a5', '#b8c4b0'][Math.floor(r() * 6)]
    tone = shades(colour)
  } else {
    colour = ['#eef2f5', '#d8dde1', '#e9e3d6', '#dfe7ee', '#c9d0d4'][Math.floor(r() * 5)]
    tone = shades(colour)
  }
  box(p, at, a0, b0, z0, da, db, h, tone)
  for (let v = 1; v < h - 1; v++) {
    const inStorey = v % 22
    for (const face of ['left', 'right'] as const) {
      const span = face === 'left' ? 2 * da : 2 * db
      for (let u = 1; u < span - 1; u++) {
        let win = false
        let glass = face === 'left' ? '#4b6378' : '#3d5266'
        if (kind === 'glass') {
          // Mullions every few pixels, a slab line every storey; the rest is glass.
          win = inStorey > 1 && u % 7 !== 0
          glass = face === 'left' ? mix(colour, '#ffffff', 0.15) : colour
          if (win && (u + v) % 29 === 0) glass = mix(glass, '#ffffff', 0.5)
        } else if (kind === 'home') {
          win = inStorey > 6 && inStorey < 18 && u % 9 > 2 && u % 9 < 7
        } else {
          win = inStorey > 5 && inStorey < 19 && u % 6 !== 0
        }
        if (!win) continue
        const lit = kind === 'glass' ? (inStorey > 3 && u % 7 > 1 ? night(u, v, face === 'left' ? 0 : 1) : null) : night(u, v, face === 'left' ? 0 : 1)
        if (face === 'left') windowPixel(p, 'left', at, a0, b0 + db, z0, u, v, glass, lit)
        else windowPixel(p, 'right', at, a0 + da, b0, z0, u, v, glass, lit)
      }
    }
  }
  if (kind === 'home') {
    // Balconies on the front: a slab and a railing under every other window.
    for (let f = 0; f < floors; f++) {
      for (let u = 2; u < 2 * da - 8; u += 18) {
        const a = a0 + u / 2
        box(p, at, a, b0 + db, z0 + f * 22 + 5, 5, 2.5, 1, shades('#d9d4ca'), false)
        for (let k = 0; k <= 10; k++) {
          const [x, y] = at(a + k / 2, b0 + db + 2.5, z0 + f * 22 + 6)
          p.rect(x, y - 4, 1, 4, k % 2 ? '#5d6878' : '#7a8696')
        }
        if (r() > 0.6) {
          const [x, y] = at(a + 1.5, b0 + db + 1.5, z0 + f * 22 + 6)
          p.ellipse(x, y - 2, 2, 2, '#4f8c50')
        }
      }
    }
  }
}
