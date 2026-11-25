import { box, cylinder, iso, onLeftFace, shades, YF } from '../iso'
import type { Prop } from '../props'
import { mix, Painter, rand } from '../px'

/**
 * Small things that make a workplace lived in: bookshelves, filing cabinets, bins, a printer,
 * boxes, a floor lamp, a water cooler, an armchair, a coat stand. And a pixel font, for the
 * name of each office.
 */

type At = (a: number, b: number, z?: number) => [number, number]

function make(da: number, db: number, top: number, draw: (p: Painter, at: At) => void): Prop['sprite'] {
  const extra = 8
  const w = 2 * (da + db) + 2 * extra
  const h = Math.ceil((da + db) * YF) + top + 2 * extra
  const ax = 2 * db + extra
  const ay = top + extra
  const p = new Painter(w, h)
  draw(p, (a, b, z = 0) => {
    const [x, y] = iso(a, b, z)
    return [ax + x, ay + y]
  })
  p.outline('#18202b')
  return { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined }
}

const shadow = (p: Painter, at: At, da: number, db: number) => p.poly([at(0, 0), at(da + 2, 0), at(da + 2, db + 2), at(0, db + 2)], '#0b1220', 0.16)

export type DecorKind = 'shelf' | 'cabinet' | 'bin' | 'printer' | 'boxes' | 'lamp' | 'cooler' | 'armchair' | 'coats' | 'aquarium'

export const DECOR_FOOT: Record<DecorKind, [number, number]> = {
  shelf: [14, 5],
  cabinet: [6, 6],
  bin: [3, 3],
  printer: [8, 6],
  boxes: [7, 6],
  lamp: [4, 4],
  cooler: [5, 5],
  armchair: [7, 7],
  coats: [4, 4],
  aquarium: [14, 5],
}

export function decor(kind: DecorKind, accent: string, seed: number): Prop {
  const r = rand(seed)
  const [da, db] = DECOR_FOOT[kind]
  let lights: Prop['lights']
  let anim: Prop['anim']
  const sprite = make(da, db, 50, (p, at) => {
    shadow(p, at, da, db)
    switch (kind) {
      case 'shelf': {
        box(p, at, 0, 0, 0, da, db, 34, shades('#c9b79a'))
        // Shelves with books of many colours, some leaning, some gaps.
        for (let z = 3; z < 32; z += 8) {
          for (let u = 1; u < 2 * da - 1;) {
            const w = 1 + Math.floor(r() * 3)
            if (r() > 0.15) {
              const c = ['#c0504d', '#3f6fb5', '#e0b14a', '#4c9a7a', '#6c5a8a', '#e8e4dc', '#2b2f36'][Math.floor(r() * 7)]
              const h = 4 + Math.floor(r() * 3)
              for (let k = 0; k < w; k++) for (let v = 0; v < h; v++) onLeftFace(p, at, 0, db, z, u + k, v, k === 0 ? mix(c, '#ffffff', 0.2) : c)
            }
            u += w + (r() > 0.8 ? 2 : 0)
          }
          for (let u = 0; u < 2 * da; u++) onLeftFace(p, at, 0, db, z - 1, u, 0, '#9a8466')
        }
        break
      }
      case 'cabinet':
        box(p, at, 0, 0, 0, da, db, 18, shades('#9aa6b4'))
        for (const z of [5, 11]) for (let u = 3; u < 9; u++) onLeftFace(p, at, 0, db, z, u, 0, '#6b7787')
        for (let u = 0; u < 2 * da; u++) onLeftFace(p, at, 0, db, 17, u, 0, accent)
        break
      case 'bin':
        cylinder(p, ...at(1.5, 1.5), 3, 7, '#5d6878', '#2b3443')
        if (r() > 0.5) p.set(...at(1, 1, 7), '#f4f0e8')
        break
      case 'printer':
        box(p, at, 0, 0, 0, da, db, 12, shades('#c3cbd5'))
        box(p, at, 0, 0, 12, da, db, 6, shades('#e3e8ed'))
        box(p, at, 2, 4, 18, 4, 2, 1, shades('#f8f8f4'), false)
        p.emitting = true
        onLeftFace(p, at, 0, db, 12, 2, 3, '#5dff9a')
        p.emitting = false
        break
      case 'boxes':
        box(p, at, 0, 0, 0, 6, 5, 6, shades('#c49a6c'))
        box(p, at, 1, 0, 6, 5, 4, 5, shades('#b38a5c'))
        if (r() > 0.4) box(p, at, 3, 3, 0, 4, 3, 4, shades('#d4aa7c'))
        for (let u = 3; u < 8; u++) onLeftFace(p, at, 0, 5, 3, u, 0, '#e8d8b0')
        break
      case 'lamp': {
        const [x, y] = at(2, 2)
        p.ellipse(x, y, 3, 1.5, '#3a4352')
        p.rect(x, y - 30, 1, 30, '#5d6878')
        p.emitting = true
        p.ellipse(x, y - 32, 4, 3, '#fff1d0')
        p.emitting = false
        lights = [{ dx: iso(2, 2)[0], dy: iso(2, 2)[1], r: 26, color: '#ffe2b0' }]
        break
      }
      case 'cooler': {
        box(p, at, 0, 0, 0, da, db, 18, shades('#e3e8ed'))
        const [x, y] = at(2.5, 2.5, 18)
        cylinder(p, x, y, 3.5, 10, '#8fd8ff', '#c4ecff', 0.75)
        p.emitting = true
        onLeftFace(p, at, 0, db, 10, 3, 0, '#5fc8ff')
        p.emitting = false
        break
      }
      case 'armchair': {
        const c = ['#7a5a8a', '#3a6a7a', '#a05a4a', '#5a7a5a'][Math.floor(r() * 4)]
        box(p, at, 0, 0, 0, da, db, 6, shades(c))
        box(p, at, 0, 0, 6, da, 2, 9, shades(c))
        box(p, at, 0, 0, 6, 2, db, 4, shades(c))
        box(p, at, da - 2, 0, 6, 2, db, 4, shades(c))
        box(p, at, 2, 2, 6, da - 4, db - 2, 1, shades(mix(c, '#ffffff', 0.15)), false)
        break
      }
      case 'aquarium': {
        // A long tank on a low cabinet: lit water, weed, pebbles; fish swim across its front.
        box(p, at, 0, 0, 0, da, db, 12, shades('#3a4352'))
        for (let u = 2; u < 2 * da - 2; u += 6) onLeftFace(p, at, 0, db, 4, u, 0, '#6b7787')
        box(p, at, 0, 0, 12, da, db, 16, shades('#2b6f8a'))
        p.emitting = true
        const tank: [number, number][] = []
        for (let u = 1; u < 2 * da - 1; u++)
          for (let v = 1; v < 15; v++) {
            const pebble = v < 3
            const weed = !pebble && (u % 9 === 3 || u % 13 === 7) && v < 4 + ((u * 7) % 9)
            onLeftFace(p, at, 0, db, 12, u, v, pebble ? ['#c9b79a', '#9a8466', '#e0d2b4'][u % 3] : weed ? '#3f9a5a' : v > 12 ? '#7fe0f0' : '#2fa8c8')
            if (!pebble && !weed && v < 12) {
              const [x0, y0] = at(0, db, 12)
              tank.push([x0 + u, y0 + Math.floor((u * YF) / 2) - v])
            }
          }
        p.emitting = false
        box(p, at, 0, 0, 28, da, db, 1, shades('#5d6878'), false)
        lights = [{ dx: iso(da / 2, db + 4)[0], dy: iso(da / 2, db + 4)[1], r: 22, color: '#7fe0f0' }]
        const extra = 8
        const ax = 2 * db + extra
        const ay = 50 + extra
        const fish = Array.from({ length: 4 }, (_, k) => ({ k, speed: 0.08 + r() * 0.1, row: 0.2 + r() * 0.6, colour: ['#ff8a3d', '#ffd24a', '#ff5a8a', '#e8f4ff'][k] }))
        anim = (ctx, x, y, t, flip) => {
          if (!tank.length) return
          for (const f of fish) {
            // Back and forth along the tank, at its own depth.
            const s = (t * f.speed + f.k * 0.31) % 2
            const along = s < 1 ? s : 2 - s
            const i = Math.floor(along * (tank.length - 1))
            const [tx0] = tank[0]
            const [tx1] = tank[tank.length - 1]
            const tx = tx0 + (tx1 - tx0) * along
            const col = tank.filter(([px]) => Math.abs(px - tx) < 0.5)
            const pick = col.length ? col[Math.min(col.length - 1, Math.floor(f.row * col.length))] : tank[i]
            ctx.fillStyle = f.colour
            ctx.fillRect(Math.round(x + flip * (pick[0] - ax)), Math.round(y + pick[1] - ay), 2, 1)
          }
        }
        break
      }
      case 'coats': {
        const [x, y] = at(2, 2)
        p.ellipse(x, y, 3, 1.5, '#3a4352')
        p.rect(x, y - 30, 1, 30, '#6b4e3a')
        for (const [dx, c] of [
          [-3, '#3a5f9a'],
          [2, '#c2443e'],
        ] as const) {
          p.rect(x + dx, y - 28, 3, 10, c)
          p.rect(x + dx + (dx > 0 ? 2 : 0), y - 28, 1, 10, mix(c, '#000000', 0.25))
        }
        break
      }
    }
  })
  return { da, db, sprite, lights, anim }
}

// A pixel font, four by five, capitals and digits.
const GLYPHS: Record<string, string> = {
  A: '0110100110011111100110011001',
  B: '1110100110011110100110011110',
  C: '0111100010001000100010000111',
  D: '1110100110011001100110011110',
  E: '1111100010001110100010001111',
  F: '1111100010001110100010001000',
  G: '0111100010001011100110010111',
  H: '1001100110011111100110011001',
  I: '1110010001000100010001001110',
  J: '0011000100010001000110010110',
  K: '1001101011001100101010011001',
  L: '1000100010001000100010001111',
  M: '1001111111011001100110011001',
  N: '1001110111011011101110011001',
  O: '0110100110011001100110010110',
  P: '1110100110011110100010001000',
  Q: '0110100110011001101101100001',
  R: '1110100110011110101010011001',
  S: '0111100010000110000100011110',
  T: '1110010001000100010001000100',
  U: '1001100110011001100110010110',
  V: '1001100110011001100101100110',
  W: '1001100110011001101111111001',
  X: '1001100101100110011010011001',
  Y: '1001100101100100010001000100',
  Z: '1111000100100100100010001111',
  '0': '0110100110111101100110010110',
  '1': '0100110001000100010001001110',
  '2': '0110100100010010010010001111',
  '3': '1110000100010110000100011110',
  '4': '1001100110011111000100010001',
  '5': '1111100010001110000100011110',
  '6': '0110100010001110100110010110',
  '7': '1111000100010010010001000100',
  '8': '0110100110010110100110010110',
  '9': '0110100110010111000100010110',
  '-': '0000000000001111000000000000',
  '.': '0000000000000000000000000100',
  "'": '0100010000000000000000000000',
}

/** A name label: capitals in the pixel font, on a dark pill edged in the office's colour. */
export function label(text: string, accent: string): HTMLCanvasElement {
  const chars = [...text.toUpperCase()].slice(0, 18)
  const width = chars.reduce((w, ch) => w + (ch === ' ' ? 3 : 5), 0) - 1
  const p = new Painter(width + 12, 13)
  for (let y = 0; y < 13; y++)
    for (let x = 0; x < width + 12; x++) {
      const corner = (x === 0 || x === width + 11) && (y === 0 || y === 12)
      if (corner) continue
      const edge = x === 0 || y === 0 || x === width + 11 || y === 12
      p.set(x, y, edge ? accent : '#10141f', edge ? 1 : 0.9)
    }
  // A dot of the office's colour, then the name.
  p.rect(3, 5, 2, 2, accent)
  let x = 7
  for (const ch of chars) {
    const g = GLYPHS[ch]
    if (g) for (let k = 0; k < 28; k++) if (g[k] === '1') p.set(x + (k % 4), 3 + Math.floor(k / 4), '#f2f4f8')
    x += ch === ' ' ? 3 : 5
  }
  return p.canvas()
}
