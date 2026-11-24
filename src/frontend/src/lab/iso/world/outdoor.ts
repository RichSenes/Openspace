import { cylinder, iso, box, shades, SQUASH } from '../iso'
import type { Prop } from '../props'
import { mix, noise, Painter, rand } from '../px'

/** What stands around the building: trees and street lamps, painted as the furniture is. */

function sprite(w: number, h: number, ax: number, ay: number, draw: (p: Painter) => void, outline = '#18202b'): Prop['sprite'] {
  const p = new Painter(w, h)
  draw(p)
  p.outline(outline)
  return { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined }
}

const LEAVES = ['#1f3d2a', '#2a5234', '#36683f', '#467f4a', '#5b9655', '#79ae64', '#9cc77a']
const PINES = ['#14302a', '#1c3f33', '#25503d', '#316346', '#427a52', '#5b9462']

/** A broad-leaved tree: a trunk and a crown of many small clumps, lit from the upper left. */
export function tree(seed: number): Prop {
  const r = rand(seed)
  const kind = r()
  if (kind < 0.3) return pine(seed)
  if (kind < 0.45) return bush(seed)
  const size = 0.8 + r() * 0.7
  const R = Math.round(13 * size)
  const trunk = Math.round(12 * size)
  const w = 2 * R + 14
  const h = trunk + Math.round(2.3 * R) + 14
  const ax = Math.round(w / 2)
  const ay = h - 6
  return {
    da: 6,
    db: 6,
    sprite: sprite(
      w,
      h,
      ax,
      ay,
      (p) => {
        p.ellipse(ax + 4, ay, R * 0.9, R * 0.9 * SQUASH, '#0b1220', 0.24)
        cylinder(p, ax, ay, 2.5, trunk + 4, '#4e3628', '#614434')
        const cx = ax
        const cy = ay - trunk - R
        // The crown: many clumps, each lit on its upper left and dark underneath, the whole crown
        // darker low down and towards the right.
        const clumps = 14 + Math.floor(r() * 8)
        const list = Array.from({ length: clumps }, () => {
          const th = r() * Math.PI * 2
          const d = Math.sqrt(r()) * R * 0.72
          return { x: cx + Math.cos(th) * d, y: cy + Math.sin(th) * d * 0.9, cr: R * (0.32 + r() * 0.2) }
        }).sort((m, n) => m.y - n.y)
        for (const c of list) {
          p.ellipse(c.x, c.y, c.cr, c.cr * 0.9, (px, py) => {
            const lx = (px - c.x) / c.cr
            const ly = (py - c.y) / c.cr
            const local = -lx * 0.55 - ly * 0.85
            const whole = (-(px - cx) * 0.5 - (py - cy) * 0.9) / R
            const grain = (noise(px / 1.7, py / 1.7, seed) - 0.5) * 0.8
            const i = Math.round(3 + local * 1.3 + whole * 1.4 + grain)
            return LEAVES[Math.max(0, Math.min(LEAVES.length - 1, i))]
          })
        }
      },
      '#132a1c',
    ),
  }
}

/** A conifer: tiers of needles, narrowing to the top. */
function pine(seed: number): Prop {
  const r = rand(seed + 1)
  const size = 0.8 + r() * 0.7
  const H = Math.round(44 * size)
  const R = Math.round(11 * size)
  const w = 2 * R + 12
  const h = H + 14
  const ax = Math.round(w / 2)
  const ay = h - 6
  return {
    da: 6,
    db: 6,
    sprite: sprite(
      w,
      h,
      ax,
      ay,
      (p) => {
        p.ellipse(ax + 3, ay, R * 0.8, R * 0.8 * SQUASH, '#0b1220', 0.24)
        cylinder(p, ax, ay, 2, 8, '#4e3628', '#614434')
        const tiers = 4
        for (let k = 0; k < tiers; k++) {
          const base = ay - 6 - k * (H / (tiers + 0.6))
          const top = base - H * 0.42
          const half = R * (1 - k * 0.2)
          p.poly(
            [
              [ax - half, base],
              [ax, top],
              [ax + half, base],
              [ax, base + half * 0.35],
            ],
            (x, y) => {
              const lx = (x - ax) / half
              const ly = (y - top) / (base - top)
              const grain = (noise(x / 1.6, y / 1.6, seed + k) - 0.5) * 0.9
              const i = Math.round(3 - lx * 1.4 - ly * 0.8 + grain + (y > base - 2 ? -1 : 0))
              return PINES[Math.max(0, Math.min(PINES.length - 1, i))]
            },
          )
        }
      },
      '#0f241c',
    ),
  }
}

/** A low bush. */
function bush(seed: number): Prop {
  const r = rand(seed + 2)
  const R = 6 + Math.round(r() * 5)
  const w = 2 * R + 10
  const h = 2 * R + 10
  const ax = Math.round(w / 2)
  const ay = h - 5
  return {
    da: 4,
    db: 4,
    sprite: sprite(
      w,
      h,
      ax,
      ay,
      (p) => {
        p.ellipse(ax + 2, ay, R, R * SQUASH, '#0b1220', 0.22)
        for (let k = 0; k < 6; k++) {
          const x = ax + (r() - 0.5) * R
          const y = ay - R * 0.7 + (r() - 0.5) * R * 0.5
          const cr = R * (0.45 + r() * 0.2)
          p.ellipse(x, y, cr, cr * 0.85, (px, py) => {
            const i = Math.round(3 - ((px - x) / cr) * 0.6 - ((py - y) / cr) * 1.1 + (noise(px / 1.5, py / 1.5, seed) - 0.5) * 0.8)
            return LEAVES[Math.max(0, Math.min(LEAVES.length - 1, i))]
          })
        }
      },
      '#132a1c',
    ),
  }
}

/** A car, pointing +a: headlights that light the road at night. */
export function car(colour: string, driving = true): Prop {
  const w = 64
  const h = 46
  const ax = 22
  const ay = 32
  return {
    da: 20,
    db: 9,
    lights: driving ? [{ dx: iso(30, 4)[0], dy: iso(30, 4)[1], r: 22, color: '#fff0c0' }] : undefined,
    sprite: sprite(w, h, ax, ay, (p) => {
      const at = (a: number, b: number, z = 0) => {
        const [x, y] = iso(a, b, z)
        return [ax + x, ay + y] as [number, number]
      }
      p.poly([at(-1, -1), at(21, -1), at(21, 10), at(-1, 10)], '#0b1220', 0.25)
      for (const [a, b] of [
        [4, 9],
        [16, 9],
      ]) {
        const [x, y] = at(a, b, 2)
        p.ellipse(x, y, 2.5, 2.5, '#15181e')
      }
      box(p, at, 0, 0, 2, 20, 9, 6, shades(colour))
      box(p, at, 5, 1, 8, 10, 7, 5, shades(mix(colour, '#0b1020', 0.1)))
      // Windows all round the cabin.
      box(p, at, 6, 1, 9, 8, 7, 3, { top: mix(colour, '#0b1020', 0.1), left: '#2b3a4c', right: '#22303f' }, false)
      p.emitting = driving
      const [hx, hy] = at(20, 2, 5)
      p.rect(hx, hy, 2, 1, '#fff4c8')
      const [gx, gy] = at(20, 7, 5)
      p.rect(gx, gy, 2, 1, '#fff4c8')
      p.emitting = false
    }),
  }
}

/** A street lamp: a thin post and a head that lights the ground at night. */
export function streetLamp(): Prop {
  const w = 20
  const h = 64
  const ax = 10
  const ay = 58
  return {
    da: 2,
    db: 2,
    lights: [{ dx: 0, dy: 0, r: 46, color: '#ffc27a' }],
    sprite: sprite(w, h, ax, ay, (p) => {
      p.ellipse(ax + 1, ay, 4, 4 * SQUASH, '#0b1220', 0.25)
      const at = (a: number, b: number, z = 0) => {
        const [x, y] = iso(a, b, z)
        return [ax + x, ay + y] as [number, number]
      }
      box(p, at, -1, -1, 0, 2, 2, 46, shades('#3a4352'))
      box(p, at, -2, -2, 46, 4, 4, 3, shades('#2b3443'))
      p.emitting = true
      const [x, y] = at(0, 0, 45)
      p.rect(x - 2, y, 4, 2, '#ffe7b0')
      p.emitting = false
    }),
  }
}

/** A bench by the path. */
export function bench(): Prop {
  const w = 40
  const h = 30
  const ax = 14
  const ay = 22
  return {
    da: 14,
    db: 4,
    sprite: sprite(w, h, ax, ay, (p) => {
      const at = (a: number, b: number, z = 0) => {
        const [x, y] = iso(a, b, z)
        return [ax + x, ay + y] as [number, number]
      }
      box(p, at, 1, 0, 0, 1, 4, 5, shades('#3a4352'))
      box(p, at, 12, 0, 0, 1, 4, 5, shades('#3a4352'))
      box(p, at, 0, 0, 5, 14, 4, 1, shades(mix('#8a6a4a', '#ffffff', 0.05)))
      box(p, at, 0, 0, 6, 14, 1, 6, shades('#8a6a4a'))
    }),
  }
}
