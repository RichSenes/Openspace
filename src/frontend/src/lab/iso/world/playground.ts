import { box, cylinder, iso, shades, SQUASH } from '../iso'
import { dot, type Prop } from '../props'
import { mix, noise, Painter, rand } from '../px'

/** Things in parks and squares: a slide, swings, a seesaw, a sandpit, benches, bike racks, planters, a fountain. */

type At = (a: number, b: number, z?: number) => [number, number]

function make(da: number, db: number, top: number, draw: (p: Painter, at: At) => void): { sprite: Prop['sprite']; ax: number; ay: number } {
  const extra = 8
  const w = 2 * (da + db) + 2 * extra
  const h = Math.ceil((da + db) * 1.34) + top + 2 * extra
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

export function slide(colour: string): Prop {
  const { sprite } = make(18, 8, 34, (p, at) => {
    p.poly([at(0, 0), at(19, 0), at(19, 9), at(0, 9)], '#0b1220', 0.18)
    // The ladder and platform, then the chute down along a.
    box(p, at, 0, 1, 0, 1, 1, 20, shades('#8d99a8'))
    box(p, at, 0, 6, 0, 1, 1, 20, shades('#8d99a8'))
    for (let z = 4; z < 20; z += 4) box(p, at, 0, 1, z, 1, 6, 1, shades('#b9c2cc'), false)
    box(p, at, 0, 1, 20, 5, 6, 2, shades(colour))
    box(p, at, 3, 1, 22, 2, 6, 6, shades(mix(colour, '#ffffff', 0.15)))
    for (let k = 0; k < 13; k++) box(p, at, 5 + k, 2, 20 - k * 1.5, 1.2, 4, 1.5, shades('#e0b14a'), false)
  })
  return { da: 18, db: 8, sprite }
}

export function swings(): Prop {
  const seats: [number, number][] = []
  const { sprite, ax, ay } = make(16, 6, 30, (p, at) => {
    p.poly([at(0, 0), at(17, 0), at(17, 7), at(0, 7)], '#0b1220', 0.15)
    for (const a of [0, 15]) {
      box(p, at, a, 0, 0, 1, 1, 24, shades('#c2443e'))
      box(p, at, a, 5, 0, 1, 1, 24, shades('#c2443e'))
    }
    box(p, at, 0, 2.5, 24, 16, 1, 1, shades('#9aa6b4'))
    for (const a of [5, 10]) {
      const [x, y] = at(a, 3, 24)
      seats.push([x, y])
    }
  })
  return {
    da: 16,
    db: 6,
    sprite,
    anim(ctx, x, y, t) {
      seats.forEach(([sx, sy], k) => {
        // Swinging: the rope and the seat sway together.
        const sway = Math.sin(t * 2.2 + k * 1.7) * 4
        for (let d = 0; d < 16; d++) dot(ctx, x + sx - ax + (sway * d) / 16, y + sy - ay + d, '#3a4352', 0.9)
        dot(ctx, x + sx - ax + sway - 2, y + sy - ay + 16, '#3a5f9a', 1, 4)
      })
    },
  }
}

export function seesaw(): Prop {
  const { sprite } = make(16, 3, 14, (p, at) => {
    p.poly([at(0, 0), at(17, 0), at(17, 4), at(0, 4)], '#0b1220', 0.15)
    box(p, at, 7, 0, 0, 2, 3, 5, shades('#5d6878'))
    for (let k = 0; k < 16; k++) box(p, at, k, 0.5, 2 + k * 0.45, 1.2, 2, 1, shades('#3fa86a'), false)
    box(p, at, 0, 0, 2, 1, 3, 3, shades('#e0b14a'))
    box(p, at, 15, 0, 9, 1, 3, 3, shades('#e0b14a'))
  })
  return { da: 16, db: 3, sprite }
}

export function sandpit(): Prop {
  const { sprite } = make(14, 14, 6, (p, at) => {
    box(p, at, 0, 0, 0, 14, 14, 3, shades('#a07c56'))
    p.poly([at(1, 1, 3), at(13, 1, 3), at(13, 13, 3), at(1, 13, 3)], (x, y) => (noise(x / 1.4, y / 1.4, 3) > 0.62 ? '#e8d6a8' : '#dcc898'))
    const [x, y] = at(5, 6, 3)
    p.ellipse(x, y - 1, 3, 2, '#e8d6a8')
    p.rect(x + 4, y - 3, 3, 3, '#c2443e')
  })
  return { da: 14, db: 14, sprite }
}

export function bench(flip = false): Prop {
  const { sprite } = make(14, 4, 12, (p, at) => {
    box(p, at, 1, 0, 0, 1, 4, 5, shades('#3a4352'))
    box(p, at, 12, 0, 0, 1, 4, 5, shades('#3a4352'))
    box(p, at, 0, 0, 5, 14, 4, 1, shades('#9a7650'))
    box(p, at, 0, 0, 6, 14, 1, 6, shades('#8a6a4a'))
  })
  void flip
  return { da: 14, db: 4, sprite }
}

export function bikeRack(seed: number): Prop {
  const r = rand(seed)
  const { sprite } = make(16, 4, 12, (p, at) => {
    for (let k = 0; k < 4; k++) {
      const [x, y] = at(2 + k * 4, 2, 0)
      p.line(x, y, x, y - 8, '#8d99a8')
      if (r() > 0.4) {
        const c = ['#c2443e', '#3a5f9a', '#2b2f36', '#e0b14a'][Math.floor(r() * 4)]
        p.ellipse(x - 4, y - 3, 3, 3, '#15181e')
        p.ellipse(x + 4, y - 1, 3, 3, '#15181e')
        p.line(x - 4, y - 3, x + 4, y - 1, c)
        p.line(x - 1, y - 6, x + 4, y - 1, c)
      }
    }
  })
  return { da: 16, db: 4, sprite }
}

/** A planter of flowers, not a tree on stone. */
export function planter(seed: number): Prop {
  const r = rand(seed)
  const { sprite } = make(10, 4, 12, (p, at) => {
    box(p, at, 0, 0, 0, 10, 4, 5, shades('#b9b3a7'))
    for (let k = 0; k < 14; k++) {
      const [x, y] = at(0.5 + r() * 9, 0.5 + r() * 3, 5)
      p.ellipse(x, y - 1, 1.6, 1.4, '#4f8c50')
      if (r() > 0.45) p.set(x, y - 2, ['#e3a9c8', '#e8d77a', '#f4f0e8', '#ff8a5c'][Math.floor(r() * 4)])
    }
  })
  return { da: 10, db: 4, sprite }
}

export function fountain(): Prop {
  const { sprite } = make(20, 20, 20, (p, at) => {
    const [cx, cy] = at(10, 10)
    cylinder(p, cx, cy, 26, 4, '#c9c3b7', '#d8d2c6')
    p.emitting = true
    p.ellipse(cx, cy - 4, 22, 22 * SQUASH, '#6ec6e8')
    p.emitting = false
    cylinder(p, cx, cy - 4, 4, 12, '#c9c3b7')
  })
  const [fx, fy] = iso(10, 10)
  return {
    da: 20,
    db: 20,
    sprite,
    anim(ctx, x, y, t) {
      for (let k = 0; k < 16; k++) {
        const ph = (t * 0.9 + k / 16) % 1
        const ang = (k / 16) * Math.PI * 2
        const d = ph * 14
        dot(ctx, x + fx + Math.cos(ang) * d, y + fy - 16 + ph * ph * 14 - ph * 10 + Math.sin(ang) * d * SQUASH, '#e6f8ff', 0.8 * (1 - ph))
      }
    },
  }
}
