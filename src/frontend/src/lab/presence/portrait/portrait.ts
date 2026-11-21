import type { Expression } from './expression'
import { curve, layer, outline, px, type Ramp } from './paint'

/**
 * Shiori's portrait: a young woman with East Asian features, long straight dark hair with a
 * side part and a small bun held by a pin with a bookmark ribbon (栞), in a cross-collared top.
 * Drawn in layers at the art's own size, back to front; the face's parts read an Expression.
 */
export const PORTRAIT_W = 128
export const PORTRAIT_H = 160

export const RAMPS: Record<string, Ramp> = {
  skin: ['#6a3e36', '#a86e5c', '#d49c80', '#ecc0a2', '#f8dcc6'],
  hair: ['#0c0c16', '#161828', '#22263c', '#343a58', '#525a80'],
  cloth: ['#0e1a2a', '#16283e', '#223a58', '#305076'],
  gold: ['#6a4a1a', '#a8782a', '#e0b050', '#f8e0a0'],
  ribbon: ['#5a1424', '#8a2234', '#c23a4a'],
}

const INK = '#120a10'
const LID = '#1e1218'
const BROW = '#2a1c1e'
const WHITE = '#f2ece6'
const WHITE_SHADE = '#c8bcb8'
const IRIS = ['#1e120e', '#3a2418', '#5e3a24']
const LIP = ['#8a3c46', '#b8585e', '#d87a7a']
const BLUSH = '#e89c90'
const SHADOW = '#b07a66'

type P = [number, number]

/** Where the face's parts sit, shifted a little by the turn of the head. */
export function faceGeometry(e: Expression) {
  const turn = e.turn * 3
  const cx = 64 + turn
  return {
    cx,
    eyes: [
      { x: 52 + turn * 1.1, y: 63, outer: -1 as const },
      { x: 76 + turn * 1.1, y: 63, outer: 1 as const },
    ],
    mouth: { x: 64 + turn * 1.3, y: 86 },
    nose: { x: 64 + turn * 1.4, y: 77 },
  }
}

function backHair(g: CanvasRenderingContext2D, e: Expression, t: number) {
  const sway = Math.sin(t * 0.8) * 1.2
  const turn = e.turn * 1.5
  g.save()
  g.translate(turn, 0)
  // The bun, behind the crown.
  g.fillStyle = '#2a2e48'
  g.beginPath()
  g.ellipse(84, 19, 10, 8, -0.3, 0, Math.PI * 2)
  g.fill()
  const grad = g.createLinearGradient(30, 14, 100, 150)
  grad.addColorStop(0, '#3c4262')
  grad.addColorStop(0.25, '#22263c')
  grad.addColorStop(1, '#0e0e18')
  g.fillStyle = grad
  g.beginPath()
  g.moveTo(64, 14)
  g.bezierCurveTo(40, 13, 27, 32, 28, 58)
  g.bezierCurveTo(27, 92, 22 + sway, 124, 18 + sway, 160)
  g.lineTo(110 + sway, 160)
  g.bezierCurveTo(106 + sway, 124, 101, 92, 100, 58)
  g.bezierCurveTo(101, 30, 88, 13, 64, 14)
  g.fill()
  g.restore()
}

function hairSheen(g: CanvasRenderingContext2D, e: Expression) {
  // Light on dark hair: short arcs that follow the curve of the head, broken where strands part.
  const turn = e.turn * 1.5
  const pts: P[] = []
  const arcs: [number, number, number][] = [
    [-2.55, -2.05, 33],
    [-1.95, -1.35, 34],
    [-1.25, -0.85, 33],
  ]
  for (const [from, to, r] of arcs)
    for (let a = from; a <= to; a += 0.02) {
      const x = 64 + turn + Math.cos(a) * r * 1.05
      const y = 50 + Math.sin(a) * r
      pts.push([x, y])
      if (a > from + 0.08 && a < to - 0.08) pts.push([x, y + 1])
    }
  px(g, '#3c4466', pts)
  px(
    g,
    '#5a6490',
    pts.filter((_, i) => i % 9 === 4),
  )
}

function body(g: CanvasRenderingContext2D) {
  // Neck and shoulders, under the clothes.
  const skin = g.createLinearGradient(56, 96, 72, 96)
  skin.addColorStop(0, '#dca88c')
  skin.addColorStop(0.6, '#d49c80')
  skin.addColorStop(1, '#b07a64')
  g.fillStyle = skin
  g.beginPath()
  g.moveTo(56, 88)
  g.lineTo(56, 108)
  g.bezierCurveTo(46, 116, 26, 118, 14, 128)
  g.lineTo(8, 160)
  g.lineTo(120, 160)
  g.lineTo(114, 128)
  g.bezierCurveTo(102, 118, 82, 116, 72, 108)
  g.lineTo(72, 88)
  g.closePath()
  g.fill()
}

function clothes(g: CanvasRenderingContext2D) {
  const grad = g.createLinearGradient(10, 118, 110, 160)
  grad.addColorStop(0, '#305076')
  grad.addColorStop(0.5, '#1c3048')
  grad.addColorStop(1, '#0e1a2a')
  g.fillStyle = grad
  g.beginPath()
  g.moveTo(6, 160)
  g.lineTo(13, 129)
  g.bezierCurveTo(26, 119, 42, 116, 52, 113)
  // The left panel crosses over the right, down to the waist: a V that is not centred.
  g.lineTo(68, 142)
  g.lineTo(79, 113)
  g.bezierCurveTo(88, 116, 102, 119, 115, 129)
  g.lineTo(122, 160)
  g.closePath()
  g.fill()
}

function collarTrim(g: CanvasRenderingContext2D) {
  const gold = '#e0b050'
  const dark = '#a8782a'
  const left = curve((x) => 113 + (x - 52) * (29 / 16), 52, 68)
  const right = curve((x) => 113 + (79 - x) * (21 / 9), 70, 79)
  px(g, gold, left)
  px(g, dark, left.map(([x, y]) => [x + 1, y] as P))
  px(g, gold, right)
  // The neck's shadow under the jaw.
  px(g, SHADOW, curve((x) => 93 + Math.abs(x - 64) * 0.15, 56, 72))
}

function face(g: CanvasRenderingContext2D, e: Expression) {
  const { cx } = faceGeometry(e)
  const s = cx - 64
  const grad = g.createLinearGradient(44 + s, 40, 86 + s, 96)
  grad.addColorStop(0, '#fbe2cc')
  grad.addColorStop(0.55, '#ecc0a2')
  grad.addColorStop(1, '#c08a70')
  g.fillStyle = grad
  g.beginPath()
  g.moveTo(42 + s, 48)
  g.bezierCurveTo(41 + s, 61, 42 + s, 73, 47 + s, 82)
  g.bezierCurveTo(51 + s, 89, 57 + s, 95, 64 + s, 96)
  g.bezierCurveTo(71 + s, 95, 77 + s, 89, 81 + s, 82)
  g.bezierCurveTo(86 + s, 73, 87 + s, 61, 86 + s, 48)
  g.bezierCurveTo(84 + s, 30, 44 + s, 30, 42 + s, 48)
  g.fill()
  // The side away from the light, a soft band along the jaw.
  const side = g.createLinearGradient(79 + s, 0, 87 + s, 0)
  side.addColorStop(0, 'rgba(176,120,98,0)')
  side.addColorStop(1, 'rgba(176,120,98,0.7)')
  g.fillStyle = side
  g.fill()
}

function blush(g: CanvasRenderingContext2D, e: Expression) {
  const { eyes } = faceGeometry(e)
  const pts: P[] = []
  for (const eye of eyes) {
    const cx = eye.x + eye.outer * 1
    const cy = 72
    for (let y = -3; y <= 3; y++)
      for (let x = -5; x <= 5; x++) {
        const d = (x / 5) ** 2 + (y / 3) ** 2
        if (d < 1 && ((x + y) & 1) === 0 && d < 0.55 + 0.4 * e.cheek) pts.push([cx + x, cy + y])
      }
  }
  px(g, BLUSH, pts)
}

/** One almond eye: lids, white, iris, light, lashes. `outer` is -1 for the eye on the left. */
function eye(g: CanvasRenderingContext2D, e: Expression, ex: number, ey: number, outer: -1 | 1) {
  const half = 6
  const inner = -outer
  // Lid curves in eye space: u from the inner corner (0) to the outer corner (1).
  const xAt = (u: number) => ex + (inner + (outer - inner) * u) * half
  const upperRest = (u: number) => ey + 1 - Math.sin(Math.PI * Math.min(1, u * 0.95 + 0.05)) * 3.6 - u * 1.6
  const lower = (u: number) => ey + 1.2 + Math.sin(Math.PI * u) * 1.6 - u * 1.3 - e.cheek * 1.3 * Math.sin(Math.PI * u)
  const upper = (u: number) => lower(u) + (upperRest(u) - lower(u)) * Math.max(0, Math.min(1.15, e.lid))
  const cols = 2 * half
  // The white and the iris, column by column between the lids.
  const irisX = ex + e.gazeX * 2.2 - outer * 0.3
  const irisY = ey - 0.5 + e.gazeY * 1.2
  for (let i = 0; i <= cols; i++) {
    const u = i / cols
    const x = Math.round(xAt(u))
    const top = Math.round(upper(u)) + 1
    const bottom = Math.round(lower(u))
    for (let y = top; y <= bottom; y++) {
      const d = Math.hypot(x - irisX, (y - irisY) * 1.05)
      let c = y <= top ? WHITE_SHADE : WHITE
      if (d < 3.4) c = d < 1.4 ? IRIS[0] : d < 2.6 ? IRIS[1] : IRIS[2]
      // The lid's shadow on the top of the iris.
      if (d < 3.4 && y <= top) c = IRIS[0]
      px(g, c, [[x, y]])
    }
  }
  // A catch-light, if the eye is open enough to show it.
  if (e.lid > 0.45) px(g, '#ffffff', [[Math.round(irisX) - outer, Math.round(irisY) - 1]])
  // The upper lid: a strong line, thicker toward the outer corner, with a small flick.
  const lid: P[] = []
  for (let i = 0; i <= cols; i++) {
    const u = i / cols
    const x = Math.round(xAt(u))
    const y = Math.round(upper(u))
    lid.push([x, y])
    if (u > 0.3) lid.push([x, y - 1])
  }
  const tip: P = [Math.round(xAt(1) + outer * 1), Math.round(upper(1) - 1)]
  lid.push(tip, [tip[0] + outer, tip[1] - 1])
  px(g, LID, lid)
  // The lower lid: a light touch at the outer half only.
  px(
    g,
    SHADOW,
    Array.from({ length: half }, (_, k) => {
      const u = 0.5 + k / (2 * half)
      return [Math.round(xAt(u)), Math.round(lower(u)) + 1] as P
    }),
  )
  // A faint fold above the lid, low and short, toward the outer corner.
  if (e.lid > 0.6)
    px(
      g,
      SHADOW,
      Array.from({ length: 5 }, (_, k) => {
        const u = 0.45 + k * 0.1
        return [Math.round(xAt(u)), Math.round(upperRest(u)) - 2] as P
      }),
    )
}

function brows(g: CanvasRenderingContext2D, e: Expression) {
  const { eyes } = faceGeometry(e)
  for (const eye of eyes) {
    const outer = eye.outer
    const lift = -e.brow * 2 + outer * e.browTilt * 1.5
    // Inner ends rise when worried, drop when frowning.
    const innerLift = e.brow < 0 ? -e.brow * 1.5 : -e.brow * 0.5
    const pts: P[] = []
    for (let k = 0; k <= 12; k++) {
      const u = k / 12
      const x = eye.x - outer * 6 + outer * 13 * u
      const y = eye.y - 9.5 + lift + innerLift * (1 - u) - Math.sin(Math.PI * u * 0.9) * 1.6 + u * 0.8
      pts.push([x, y])
      if (u < 0.55) pts.push([x, y + 1])
    }
    px(g, BROW, pts)
  }
}

function nose(g: CanvasRenderingContext2D, e: Expression) {
  const { nose: n } = faceGeometry(e)
  // A soft bridge in shadow on the far side, a lit tip, small nostrils.
  px(g, SHADOW, [
    [n.x + 3, n.y - 8],
    [n.x + 3, n.y - 6],
    [n.x + 3, n.y - 4],
    [n.x + 3, n.y - 2],
    [n.x + 2, n.y + 1],
    [n.x - 2, n.y + 2],
    [n.x + 2, n.y + 2],
    [n.x - 1, n.y + 3],
    [n.x, n.y + 3],
    [n.x + 1, n.y + 3],
  ])
  px(g, '#fbe6d4', [
    [n.x, n.y],
    [n.x - 1, n.y],
  ])
}

function mouth(g: CanvasRenderingContext2D, e: Expression) {
  const { mouth: m } = faceGeometry(e)
  const half = 6 + e.wide * 1.2 - Math.max(0, -e.wide) * 1.5
  const corner = -e.smile * 1.6
  const openPx = Math.round(e.open * 4)
  // The line between the lips, with the corners lifted by a smile.
  const line = (x: number) => {
    const u = (x - m.x) / half
    return m.y + corner * u * u + (1 - u * u) * 0.3
  }
  const upperLip: P[] = []
  const lowerLip: P[] = []
  const inside: P[] = []
  for (let x = Math.round(m.x - half); x <= Math.round(m.x + half); x++) {
    const u = Math.abs(x - m.x) / half
    const y = Math.round(line(x))
    // A small bow at the middle of the upper lip.
    const bow = u < 0.25 ? -1 : 0
    upperLip.push([x, y - 1])
    if (u < 0.75) upperLip.push([x, y - 2 + (u < 0.12 ? 1 : bow + 1)])
    for (let k = 1; k <= openPx; k++) inside.push([x, y + k - 1])
    if (u < 0.8) lowerLip.push([x, y + openPx + 1])
    if (u < 0.55) lowerLip.push([x, y + openPx + 2])
  }
  px(g, LIP[1], upperLip)
  px(g, LIP[2], lowerLip)
  if (openPx) px(g, '#3a1418', inside)
  px(g, LIP[0], curve(line, m.x - half, m.x + half))
  // The light on the lower lip.
  px(g, '#e8a0a0', [[Math.round(m.x) - 1, Math.round(line(m.x)) + openPx + 2]])
  // The chin's soft shadow.
  px(g, SHADOW, [
    [m.x - 1, m.y + 6 + openPx],
    [m.x, m.y + 6 + openPx],
    [m.x + 1, m.y + 6 + openPx],
  ])
}

function frontHair(g: CanvasRenderingContext2D, e: Expression, t: number) {
  const turn = e.turn * 4
  const sway = Math.sin(t * 0.9 + 0.6) * 1.5
  g.save()
  g.translate(turn, 0)
  const grad = g.createLinearGradient(40, 26, 90, 150)
  grad.addColorStop(0, '#343a58')
  grad.addColorStop(0.3, '#1c2034')
  grad.addColorStop(1, '#0c0c16')
  g.fillStyle = grad
  // Bangs swept from a part on her right across the forehead, ending in points.
  g.beginPath()
  g.moveTo(55, 26)
  g.bezierCurveTo(68, 28, 82, 34, 89, 50)
  g.lineTo(88, 60)
  g.lineTo(84, 52)
  g.lineTo(80, 55)
  g.lineTo(76, 48)
  g.lineTo(71, 51)
  g.lineTo(67, 46)
  g.lineTo(62, 48)
  g.lineTo(58, 44)
  g.lineTo(53, 46)
  g.bezierCurveTo(48, 46, 44, 51, 43, 60)
  g.bezierCurveTo(40, 46, 43, 33, 55, 26)
  g.fill()
  // Locks in front of the shoulders, framing the face.
  g.beginPath()
  g.moveTo(43, 44)
  g.bezierCurveTo(37, 62, 38, 92, 33 + sway, 128)
  g.lineTo(31 + sway, 150)
  g.lineTo(37 + sway, 140)
  g.lineTo(39 + sway, 148)
  g.bezierCurveTo(42, 118, 46, 86, 45, 58)
  g.closePath()
  g.fill()
  g.beginPath()
  g.moveTo(86, 46)
  g.bezierCurveTo(91, 62, 91, 92, 95 + sway, 126)
  g.lineTo(98 + sway, 146)
  g.lineTo(92 + sway, 138)
  g.lineTo(90 + sway, 144)
  g.bezierCurveTo(87, 114, 84, 84, 84, 58)
  g.closePath()
  g.fill()
  g.restore()
}

function strands(g: CanvasRenderingContext2D, e: Expression, t: number) {
  // A few lighter strands along each lock, broken into lengths, so the hair reads as hair.
  const turn = e.turn * 4
  const sway = Math.sin(t * 0.9 + 0.6) * 1.5
  const pts: P[] = []
  const lock = (x0: number, x1: number, bend: number, from: number, to: number, gap: number) => {
    for (let y = from; y < to; y++) {
      const k = (y - from) / (to - from)
      if (Math.floor(y / gap) % 3 === 2) continue
      pts.push([x0 + (x1 - x0) * k + Math.sin(k * Math.PI) * bend + sway * k + turn, y])
    }
  }
  lock(41, 36, -1, 62, 128, 9)
  lock(43, 40, 0, 80, 140, 7)
  lock(87, 93, 1, 60, 126, 8)
  lock(85, 89, 0, 84, 138, 11)
  px(g, '#2c3250', pts)
  // The bangs: a couple of strands sweeping with them.
  const bang: P[] = []
  for (let x = 58; x < 84; x++) {
    const k = (x - 58) / 26
    if (x % 6 === 0) continue
    bang.push([x + turn, 30 + k * 11 + k * k * 5])
  }
  px(g, '#343a58', bang)
}

function pin(g: CanvasRenderingContext2D, e: Expression, t: number) {
  // A hairpin through the bun, and a bookmark ribbon hanging from it with a tassel.
  const turn = e.turn * 1.5
  const swing = Math.sin(t * 1.3) * 1.2
  px(g, '#e0b050', curve((x) => 8 + (x - 72) * 0.62, 72 + turn, 98 + turn))
  px(g, '#a8782a', curve((x) => 9 + (x - 72) * 0.62, 74 + turn, 97 + turn))
  px(g, '#f8e0a0', [[72 + turn, 8], [73 + turn, 8]])
  const top: P = [97 + turn, 25]
  const ribbon: P[] = []
  const dark: P[] = []
  for (let y = 0; y < 16; y++) {
    const x = top[0] + Math.round((y / 16) * swing * 1.5)
    ribbon.push([x, top[1] + y], [x + 1, top[1] + y])
    dark.push([x + 2, top[1] + y])
  }
  px(g, '#c23a4a', ribbon)
  px(g, '#8a2234', dark)
  const end = top[0] + Math.round(swing * 1.5)
  px(g, '#e0b050', [
    [end, top[1] + 16],
    [end + 1, top[1] + 16],
    [end + 2, top[1] + 16],
  ])
  px(g, '#a8782a', [
    [end, top[1] + 17],
    [end + 1, top[1] + 18],
    [end + 2, top[1] + 17],
    [end + 1, top[1] + 19],
  ])
}

/** Draws the whole portrait into a PORTRAIT_W × PORTRAIT_H canvas. */
export function drawPortrait(g: CanvasRenderingContext2D, e: Expression, t = 0) {
  g.clearRect(0, 0, PORTRAIT_W, PORTRAIT_H)
  g.imageSmoothingEnabled = false
  layer(g, RAMPS.hair, (c) => backHair(c, e, t))
  hairSheen(g, e)
  layer(g, RAMPS.skin, body)
  layer(g, RAMPS.cloth, clothes)
  collarTrim(g)
  layer(g, RAMPS.skin, (c) => face(c, e))
  blush(g, e)
  const { eyes } = faceGeometry(e)
  for (const ey of eyes) eye(g, e, Math.round(ey.x), ey.y, ey.outer)
  brows(g, e)
  nose(g, e)
  mouth(g, e)
  layer(g, RAMPS.hair, (c) => frontHair(c, e, t))
  strands(g, e, t)
  pin(g, e, t)
  outline(g, INK)
}
