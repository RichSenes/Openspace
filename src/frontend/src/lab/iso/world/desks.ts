import { box, cylinder, iso, onLeftFace, shades, YF } from '../iso'
import type { Light, Prop } from '../props'
import { mix, Painter, rand } from '../px'

/**
 * The lab's desks, turned towards you: whoever works sits behind them and faces the person
 * looking at the lab, so you see faces, not backs. The monitors' backs face you, a little light
 * showing round them; the desks' front panels carry the office's colour. Drawn along a; mirrored,
 * they turn the other way, so the room varies between left and right.
 */

type At = (a: number, b: number, z?: number) => [number, number]

const WHITE = '#dfe6ec'

function paint(da: number, db: number, top: number, draw: (p: Painter, at: At) => void): Prop['sprite'] {
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

const lamp = (a: number, b: number, r: number, color: string): Light => {
  const [dx, dy] = iso(a, b)
  return { dx, dy, r, color }
}

/** A monitor seen from behind: its back panel and stand towards you, a rim of light round it. */
function monitorBack(p: Painter, at: At, a: number, b: number, z: number, wide: number, high: number, accent: string) {
  box(p, at, a + wide / 2 - 1, b - 1, z, 2, 2, 5, shades('#4a5466'))
  box(p, at, a + wide / 2 - 3, b - 2, z, 6, 4, 1, shades('#4a5466'))
  box(p, at, a, b, z + 5, wide, 1.5, high, shades('#3a4352'))
  // The back panel: a lighter plate with vents, and the brand's dot.
  for (let u = 2; u < wide * 2 - 2; u++)
    for (let v = 2; v < high - 2; v++) {
      const vent = v > high - 6 && u % 3 === 0
      onLeftFace(p, at, a, b + 1.5, z + 5, u, v, vent ? '#2b3443' : '#4a5566')
    }
  p.emitting = true
  // The screen's light, spilling round the edge at the top.
  for (let u = 1; u < wide * 2 - 1; u++) onLeftFace(p, at, a, b + 1.5, z + 5, u, high - 1, '#7fd6ff', 0.7)
  onLeftFace(p, at, a, b + 1.5, z + 5, wide, 4, accent)
  p.emitting = false
}

function frontPanel(p: Painter, at: At, da: number, db: number, accent: string) {
  // A modesty panel across the front, the office's colour along its top.
  box(p, at, 1, db - 2, 4, da - 2, 1, 16, shades('#c9d1da'))
  for (let u = 2; u < 2 * (da - 2); u++) onLeftFace(p, at, 1, db - 1, 4, u, 14, accent)
}

/** A long bench with three screens; three people sit behind it, facing you. */
export function benchFacing(accent: string, seed = 1): Prop {
  const da = 40
  const db = 12
  const r = rand(seed)
  const sprite = paint(da, db, 50, (p, at) => {
    p.poly([at(0, 0), at(da + 2, 0), at(da + 2, db + 2), at(0, db + 2)], '#0b1220', 0.16)
    box(p, at, 1, 1, 0, 2, db - 2, 20, shades('#9aa6b4'))
    box(p, at, da - 3, 1, 0, 2, db - 2, 20, shades('#9aa6b4'))
    frontPanel(p, at, da, db, accent)
    box(p, at, 0, 0, 20, da, db, 3, shades(WHITE))
    for (let k = 0; k < 3; k++) {
      box(p, at, 4 + k * 13, 2, 23, 8, 3, 1, shades('#c4ccd6'), false)
      monitorBack(p, at, 3 + k * 13, db - 4, 23, 10, 13, accent)
    }
    // A mug, papers, a plant on one end.
    cylinder(p, ...at(1.5 + r() * 2, 3, 23), 2, 4, '#f2f2f0')
    p.poly([at(36, 2, 23), at(39, 2, 23), at(39, 6, 23), at(36, 6, 23)], '#f6f4ec')
  })
  return { sprite, da, db, lights: [0, 1, 2].map((k) => lamp(8 + k * 13, -3, 24, '#5fc8ff')) }
}

/** A lab bench with a microscope and tubes; two people work behind it, facing you. */
export function microscopeFacing(accent: string): Prop {
  const da = 30
  const db = 12
  const sprite = paint(da, db, 50, (p, at) => {
    p.poly([at(0, 0), at(da + 2, 0), at(da + 2, db + 2), at(0, db + 2)], '#0b1220', 0.16)
    box(p, at, 0, 0, 0, da, db, 20, shades('#c9d1da'))
    for (let u = 2; u < da * 2 - 2; u += 20) for (let v = 3; v < 18; v++) onLeftFace(p, at, 0, db, 0, u, v, '#aab4c0')
    for (let u = 0; u < da * 2; u++) onLeftFace(p, at, 0, db, 0, u, 17, accent)
    box(p, at, 0, 0, 20, da, db, 2, shades('#3a4352'))
    // The microscope, its eyepiece towards the one behind the bench.
    box(p, at, 4, 4, 22, 6, 6, 2, shades('#e9edf1'))
    box(p, at, 5, 8, 24, 2, 2, 12, shades('#e9edf1'))
    box(p, at, 6, 6, 33, 4, 2, 3, shades('#e9edf1'))
    box(p, at, 8, 6, 28, 2, 2, 6, shades('#3a4352'))
    box(p, at, 14, 7, 22, 10, 3, 3, shades('#d6dce3'))
    ;['#e0607e', '#5fd18e', '#62b5ff', '#f0c04a', '#b27cff'].forEach((c, k) => {
      const [x, y] = at(15 + k * 2, 8, 25)
      p.rect(x, y - 7, 1, 7, '#e8f6ff')
      p.rect(x, y - 4, 1, 4, c)
    })
    const [bx, by] = at(26, 4, 22)
    cylinder(p, bx, by, 4, 9, '#cfeaff', '#e8f6ff', 0.55)
    p.emitting = true
    cylinder(p, bx, by, 4, 5, accent, mix(accent, '#ffffff', 0.4), 0.8)
    p.emitting = false
  })
  return { sprite, da, db, lights: [lamp(26, 4, 18, accent)] }
}

/** An L-shaped desk: its long arm towards you with a wide screen, a side arm with books; one person in the corner, facing you. */
export function cornerFacing(accent: string, seed = 2): Prop {
  const da = 30
  const db = 26
  const r = rand(seed)
  const sprite = paint(da, db, 50, (p, at) => {
    p.poly([at(0, 14), at(da + 2, 14), at(da + 2, db + 2), at(0, db + 2)], '#0b1220', 0.16)
    p.poly([at(0, 0), at(14, 0), at(14, 14), at(0, 14)], '#0b1220', 0.16)
    // The side arm, at the back on the left.
    box(p, at, 1, 1, 0, 10, 2, 20, shades('#9aa6b4'))
    box(p, at, 0, 0, 20, 12, 14, 3, shades(WHITE))
    for (let k = 0; k < 4; k++) box(p, at, 2, 2 + k * 2, 23, 6, 2, 7 + Math.floor(r() * 3), shades(['#c0504d', '#3f6fb5', '#e0b14a', '#4c9a7a'][k]), false)
    box(p, at, 3, 10, 23, 6, 3, 3, shades('#f4f2ea'), false)
    // The long arm, at the front.
    box(p, at, 1, 15, 0, 2, 10, 20, shades('#9aa6b4'))
    box(p, at, da - 3, 15, 0, 2, 10, 20, shades('#9aa6b4'))
    box(p, at, 1, db - 2, 4, da - 2, 1, 16, shades('#c9d1da'))
    for (let u = 2; u < 2 * (da - 2); u++) onLeftFace(p, at, 1, db - 1, 4, u, 14, accent)
    box(p, at, 0, 14, 20, da, 12, 3, shades(WHITE))
    monitorBack(p, at, 10, db - 5, 23, 16, 15, accent)
    // A lamp and a plant at the ends.
    cylinder(p, ...at(4, 20, 23), 3, 5, '#d8dde3')
    p.line(...at(4, 20, 28), ...at(7, 18, 40), '#5d6878')
    box(p, at, 5, 16, 38, 4, 4, 3, shades(accent))
    cylinder(p, ...at(27, 18, 23), 3, 5, '#c9cfd6')
    for (let k = 0; k < 14; k++) {
      const [x, y] = at(27, 18, 28)
      p.ellipse(x - 4 + r() * 8, y - 3 - r() * 9, 2, 1.5, ['#3d8f5a', '#57b06f', '#2f7048'][k % 3])
    }
  })
  return { sprite, da, db, lights: [lamp(20, 8, 30, '#7fc8ff'), lamp(7, 20, 26, '#ffd9a0')] }
}
