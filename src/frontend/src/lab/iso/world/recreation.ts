import { box, iso, onLeftFace, shades, YF } from '../iso'
import { dot, type Prop } from '../props'
import { mix, Painter, rand } from '../px'

/**
 * The break room's games and comforts: a ping-pong table, a table football, an arcade cabinet,
 * a vending machine, beanbags. Things several agents do together: where friendships and grudges
 * will come from.
 */

type At = (a: number, b: number, z?: number) => [number, number]

function make(da: number, db: number, top: number, draw: (p: Painter, at: At) => void): { sprite: Prop['sprite']; ax: number; ay: number } {
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
  return { sprite: { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined }, ax, ay }
}

const shadow = (p: Painter, at: At, da: number, db: number) => p.poly([at(0, 0), at(da + 2, 0), at(da + 2, db + 2), at(0, db + 2)], '#0b1220', 0.16)

export const PING = { da: 22, db: 12 }
export const FOOS = { da: 14, db: 8 }

/** A ping-pong table, long along a: a player at each end. */
export function pingPong(): Prop {
  const { da, db } = PING
  const { sprite } = make(da, db, 20, (p, at) => {
    shadow(p, at, da, db)
    for (const [a, b] of [
      [1, 1],
      [da - 2, 1],
      [1, db - 2],
      [da - 2, db - 2],
    ])
      box(p, at, a, b, 0, 1, 1, 10, shades('#3a4352'))
    box(p, at, 0, 0, 10, da, db, 1.5, { top: '#2f6fa8', left: '#245a8a', right: '#1d4a72' })
    // White edges and the centre line, then the net.
    p.poly([at(0, db / 2 - 0.25, 11.5), at(da, db / 2 - 0.25, 11.5), at(da, db / 2 + 0.25, 11.5), at(0, db / 2 + 0.25, 11.5)], '#e8eef4')
    for (let u = 0; u < 2 * da; u++) onLeftFace(p, at, 0, db, 10, u, 1, '#e8eef4')
    box(p, at, da / 2 - 0.25, -1, 11.5, 0.5, db + 2, 3, { top: '#f4f6f8', left: '#d8dde3', right: '#c4cad1' }, false)
  })
  return { da, db, sprite }
}

/** A table football, long along a: two players each side. */
export function foosball(): Prop {
  const { da, db } = FOOS
  const rods: [number, number][] = []
  const { sprite, ax, ay } = make(da, db, 26, (p, at) => {
    shadow(p, at, da, db)
    box(p, at, 1, 1, 0, 2, db - 2, 9, shades('#6b4e3a'))
    box(p, at, da - 3, 1, 0, 2, db - 2, 9, shades('#6b4e3a'))
    box(p, at, 0, 0, 9, da, db, 6, shades('#8a6a4a'))
    p.poly([at(1, 1, 15), at(da - 1, 1, 15), at(da - 1, db - 1, 15), at(1, db - 1, 15)], (x, y) => ((x + y) % 7 === 0 ? '#4f9a58' : '#3f8a4a'))
    for (let k = 0; k < 4; k++) {
      const a = 2.5 + k * 3
      p.line(...at(a, -2, 17), ...at(a, db + 2, 17), '#c9cfd6')
      rods.push(at(a, db / 2, 17))
    }
  })
  return {
    da,
    db,
    sprite,
    // The little players turn as the rods are spun.
    anim(ctx, x, y, t, flip) {
      rods.forEach(([rx, ry], k) => {
        const spin = Math.sin(t * 7 + k * 1.3)
        for (let j = -1; j <= 1; j++) dot(ctx, x + flip * (rx - ax) + j * 3, y + ry - ay + j * 2 + Math.round(spin), k % 2 ? '#d23a3a' : '#3a5f9a', 1, 2)
      })
    },
  }
}

/** An arcade cabinet with a lit screen. */
export function arcade(seed: number): Prop {
  const r = rand(seed)
  const colour = ['#5a3a8a', '#2f5f9a', '#9a2f4a'][Math.floor(r() * 3)]
  const da = 6
  const db = 6
  const screen: [number, number][] = []
  const { sprite, ax, ay } = make(da, db, 40, (p, at) => {
    shadow(p, at, da, db)
    box(p, at, 0, 0, 0, da, db, 34, shades(colour))
    box(p, at, 0, db - 1, 14, da, 2, 2, shades('#2b2f36'))
    p.emitting = true
    for (let u = 2; u < 2 * da - 2; u++)
      for (let v = 0; v < 10; v++) {
        onLeftFace(p, at, 0, db, 20, u, v, v === 9 ? '#ffd27a' : '#0d1a2a')
        if (v > 1 && v < 8) {
          const [x0, y0] = at(0, db, 20)
          screen.push([x0 + u, y0 + Math.floor((u * YF) / 2) - v])
        }
      }
    for (let u = 1; u < 2 * da - 1; u++) onLeftFace(p, at, 0, db, 31, u, 2, mix(colour, '#ffffff', 0.6))
    p.emitting = false
  })
  return {
    da,
    db,
    sprite,
    anim(ctx, x, y, t, flip) {
      // Something chases something on the screen.
      const k = Math.floor(t * 6)
      screen.forEach(([sx, sy], i) => {
        if ((i + k) % 11 === 0) dot(ctx, x + flip * (sx - ax), y + sy - ay, i % 3 ? '#5dff9a' : '#ff5a8a')
      })
    },
  }
}

/** A vending machine, lit from inside. */
export function vending(): Prop {
  const da = 6
  const db = 7
  const { sprite } = make(da, db, 40, (p, at) => {
    shadow(p, at, da, db)
    box(p, at, 0, 0, 0, da, db, 34, shades('#c2443e'))
    p.emitting = true
    for (let u = 2; u < 2 * da - 1; u++)
      for (let v = 8; v < 31; v++) {
        const shelf = v % 6 === 0
        const can = !shelf && v % 6 > 1 && u % 3 !== 0
        onLeftFace(p, at, 0, db, 0, u, v, shelf ? '#8d99a8' : can ? ['#ffd27a', '#5fd7e6', '#7be3a8', '#f4f0e8'][(u + Math.floor(v / 6)) % 4] : '#e8f6ff')
      }
    p.emitting = false
  })
  return { da, db, sprite, lights: [{ dx: iso(3, 10)[0], dy: iso(3, 10)[1], r: 16, color: '#ffe8d0' }] }
}

/** A beanbag, slumped. */
export function beanbag(seed: number): Prop {
  const r = rand(seed)
  const colour = ['#e07a5f', '#3a8a7a', '#e0b14a', '#6c5a8a'][Math.floor(r() * 4)]
  const { sprite } = make(7, 7, 12, (p, at) => {
    const [x, y] = at(3.5, 3.5)
    p.ellipse(x + 1, y + 1, 8, 4, '#0b1220', 0.2)
    p.ellipse(x, y - 4, 8, 6, (px, py) => (py < y - 6 && px < x ? mix(colour, '#ffffff', 0.25) : px > x + 3 ? mix(colour, '#000000', 0.2) : colour))
    p.ellipse(x - 1, y - 8, 5, 3, mix(colour, '#ffffff', 0.12))
  })
  return { da: 7, db: 7, sprite }
}
