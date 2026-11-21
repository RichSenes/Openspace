import type { Expression } from './expression'
import { curve, layer, outline, px, type Ramp } from './paint'

/**
 * Shiori's portrait: a young woman with East Asian features, dark hair gathered in a bun at the
 * centre of her head and held by a pin with a bookmark ribbon (栞), two locks falling in front;
 * an elegant dark kimono with a white under-collar, small flowers, and the top of the obi.
 * Drawn in layers at the art's own size, back to front; the face's parts read an Expression.
 */
export const PORTRAIT_W = 128
export const PORTRAIT_H = 176

export const RAMPS: Record<string, Ramp> = {
  skin: ['#6a3e36', '#a86e5c', '#d49c80', '#ecc0a2', '#f8dcc6'],
  hair: ['#0c0c16', '#161828', '#22263c', '#343a58', '#525a80'],
  cloth: ['#0a0810', '#141020', '#201a32', '#30284a', '#44396a'],
  obi: ['#4a0e1a', '#7a1a2a', '#b02a3a', '#d84a50'],
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

function backHair(g: CanvasRenderingContext2D, e: Expression) {
  const turn = e.turn * 1.5
  g.save()
  g.translate(turn, 0)
  const grad = g.createLinearGradient(40, 4, 90, 90)
  grad.addColorStop(0, '#3c4262')
  grad.addColorStop(0.35, '#22263c')
  grad.addColorStop(1, '#0e0e18')
  g.fillStyle = grad
  // The bun, at the centre of the head, a little back.
  g.beginPath()
  g.ellipse(64, 15, 14, 11, 0, 0, Math.PI * 2)
  g.fill()
  // The hair drawn up: the skull's outline, tucked in at the nape behind the ears.
  g.beginPath()
  g.moveTo(64, 20)
  g.bezierCurveTo(44, 20, 33, 34, 35, 56)
  g.bezierCurveTo(36, 70, 40, 80, 46, 86)
  g.lineTo(82, 86)
  g.bezierCurveTo(88, 80, 92, 70, 93, 56)
  g.bezierCurveTo(95, 34, 84, 20, 64, 20)
  g.fill()
  g.restore()
}

function hairSheen(g: CanvasRenderingContext2D, e: Expression) {
  // Light on dark hair: arcs that follow the head, and the coil of the bun.
  const turn = e.turn * 1.5
  const pts: P[] = []
  const arcs: [number, number, number][] = [
    [-2.6, -2.1, 30],
    [-1.95, -1.4, 31],
    [-1.25, -0.8, 30],
  ]
  for (const [from, to, r] of arcs)
    for (let a = from; a <= to; a += 0.02) {
      const x = 64 + turn + Math.cos(a) * r * 1.02
      const y = 52 + Math.sin(a) * r
      pts.push([x, y])
      if (a > from + 0.08 && a < to - 0.08) pts.push([x, y + 1])
    }
  for (let a = -2.7; a <= -0.5; a += 0.03) pts.push([64 + turn + Math.cos(a) * 10, 15 + Math.sin(a) * 7])
  for (let a = 0.4; a <= 2.6; a += 0.04) pts.push([64 + turn + Math.cos(a) * 7, 13 + Math.sin(a) * 4])
  px(g, '#3c4466', pts)
  px(g, '#5a6490', pts.filter((_, i) => i % 9 === 4))
}

function body(g: CanvasRenderingContext2D) {
  // Neck, collarbones and shoulders, under the kimono.
  const skin = g.createLinearGradient(56, 96, 74, 96)
  skin.addColorStop(0, '#dca88c')
  skin.addColorStop(0.6, '#d49c80')
  skin.addColorStop(1, '#b07a64')
  g.fillStyle = skin
  g.beginPath()
  g.moveTo(57, 88)
  g.lineTo(57, 106)
  g.bezierCurveTo(48, 112, 30, 116, 18, 126)
  g.lineTo(12, 176)
  g.lineTo(116, 176)
  g.lineTo(110, 126)
  g.bezierCurveTo(98, 116, 80, 112, 71, 106)
  g.lineTo(71, 88)
  g.closePath()
  g.fill()
}

/** The kimono: crossed collar, a soft fullness at the chest, sleeves falling from the shoulders. */
function clothes(g: CanvasRenderingContext2D) {
  const grad = g.createLinearGradient(20, 112, 100, 176)
  grad.addColorStop(0, '#30284a')
  grad.addColorStop(0.5, '#1c1630')
  grad.addColorStop(1, '#0e0b18')
  g.fillStyle = grad
  g.beginPath()
  g.moveTo(10, 176)
  g.lineTo(17, 127)
  g.bezierCurveTo(28, 118, 42, 114, 51, 112)
  // The left panel over the right, closing just under the bust.
  g.bezierCurveTo(56, 124, 62, 138, 68, 152)
  g.bezierCurveTo(71, 140, 74, 124, 78, 112)
  g.bezierCurveTo(88, 114, 101, 118, 111, 127)
  g.lineTo(118, 176)
  g.closePath()
  g.fill()
  g.save()
  g.globalCompositeOperation = 'source-atop'
  // The sleeves part from the body at the shoulder seam.
  g.fillStyle = 'rgba(6,4,10,0.7)'
  g.fillRect(24, 132, 2, 44)
  g.fillRect(103, 132, 2, 44)
  g.restore()
}

/** The chest drawn as folds of cloth: a soft light along the top, a shadow line beneath. */
function chest(g: CanvasRenderingContext2D) {
  for (const [cx, side] of [
    [46, -1],
    [83, 1],
  ] as const) {
    const under = curve((x) => 150 - Math.sqrt(Math.max(0, 1 - ((x - cx) / 13) ** 2)) * 4, cx - 12, cx + 12)
    px(g, '#07050c', under)
    px(g, '#0e0b18', under.map(([x, y]) => [x, y + 1] as P))
    const light = curve((x) => 137 - Math.sqrt(Math.max(0, 1 - ((x - cx) / 10) ** 2)) * 3, cx - 8 + side * 2, cx + 6 + side * 2)
    px(g, '#30284a', light.filter((_, i) => i % 3 !== 0))
  }
}

/** The obi, high under the bust, with its cord. */
function obi(g: CanvasRenderingContext2D) {
  const grad = g.createLinearGradient(0, 160, 0, 176)
  grad.addColorStop(0, '#b02a3a')
  grad.addColorStop(1, '#4a0e1a')
  g.fillStyle = grad
  g.beginPath()
  g.moveTo(22, 164)
  g.bezierCurveTo(48, 160, 80, 160, 106, 164)
  g.lineTo(108, 176)
  g.lineTo(20, 176)
  g.closePath()
  g.fill()
}

function collarTrim(g: CanvasRenderingContext2D) {
  // The white under-collar showing inside the kimono's, then the kimono's own edge.
  const left = (x: number) => 112 + (x - 51) * (40 / 17) * (0.85 + 0.15 * ((x - 51) / 17))
  const right = (x: number) => 112 + (78 - x) * (40 / 10)
  const inner: P[] = []
  const edge: P[] = []
  for (const [x, y] of curve(left, 51, 67)) {
    inner.push([x + 1, y], [x + 2, y])
    edge.push([x - 1, y])
  }
  for (const [x, y] of curve(right, 69, 78)) {
    if (y > 150) continue
    inner.push([x - 1, y], [x - 2, y])
    edge.push([x + 1, y])
  }
  px(g, '#ece6e0', inner)
  px(g, '#b8b0b0', inner.filter((_, i) => i % 2 === 1))
  px(g, '#c8a04a', edge)
  // The neck's shadow under the jaw.
  px(g, SHADOW, curve((x) => 93 + Math.abs(x - 64) * 0.15, 57, 71))
  // Collarbones, faintly.
  px(g, SHADOW, [
    ...curve((x) => 110 - (x - 40) * 0.12, 42, 54),
    ...curve((x) => 110 - (88 - x) * 0.12, 74, 86),
  ])
}

/** Small flowers on the kimono, and a cord across the obi. */
function pattern(g: CanvasRenderingContext2D) {
  const flowers: [number, number, number][] = [
    [30, 140, 0],
    [20, 160, 1],
    [38, 168, 0],
    [96, 136, 1],
    [108, 152, 0],
    [92, 170, 1],
    [14, 140, 1],
    [114, 140, 0],
  ]
  for (const [x, y, k] of flowers) {
    if (y > 160 && x > 20 && x < 106) continue
    const petal = k ? '#c84a5a' : '#e0a0b0'
    px(g, petal, [
      [x, y - 1],
      [x - 1, y],
      [x + 1, y],
      [x - 1, y + 1],
      [x + 1, y + 1],
    ])
    px(g, '#e0b050', [[x, y]])
  }
  // The cord, with its knot a little off centre.
  px(g, '#e0b050', curve((x) => 170 - Math.sin((x - 22) / 86 * Math.PI) * 2, 22, 106))
  px(g, '#a8782a', curve((x) => 171 - Math.sin((x - 22) / 86 * Math.PI) * 2, 22, 106))
  px(g, '#f8e0a0', [
    [72, 167],
    [73, 167],
    [71, 168],
    [74, 168],
    [72, 169],
    [73, 169],
  ])
  px(g, '#1a0a14', curve((x) => 163 - Math.sin((x - 22) / 86 * Math.PI) * 3.5, 22, 106))
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
  // A small nose: a soft shadow down the far side of the bridge, a rounded tip, two nostrils.
  px(g, '#d8a48c', [
    [n.x + 2, n.y - 7],
    [n.x + 2, n.y - 5],
    [n.x + 2, n.y - 3],
    [n.x + 2, n.y - 2],
  ])
  px(g, SHADOW, [
    [n.x + 2, n.y],
    [n.x + 2, n.y + 1],
    [n.x + 1, n.y + 2],
    [n.x - 2, n.y + 2],
  ])
  px(g, '#8a5048', [
    [n.x - 1, n.y + 2],
    [n.x + 1, n.y + 2],
  ])
  px(g, '#fbe6d4', [
    [n.x - 1, n.y - 1],
    [n.x, n.y],
  ])
}

/**
 * Lips with their shape: a cupid's bow on the upper lip, a fuller lower lip with its light, the
 * line between them carrying the expression (smile, pout, open).
 */
function mouth(g: CanvasRenderingContext2D, e: Expression) {
  const { mouth: m } = faceGeometry(e)
  const half = 6.5 + e.wide * 1.2 - Math.max(0, -e.wide) * 2
  const corner = -e.smile * 1.8
  const openPx = Math.round(e.open * 4)
  const line = (x: number) => {
    const u = (x - m.x) / half
    return m.y + corner * u * u + (1 - u * u) * 0.3
  }
  const upper: P[] = []
  const upperDark: P[] = []
  const lower: P[] = []
  const lowerLight: P[] = []
  const inside: P[] = []
  for (let x = Math.round(m.x - half); x <= Math.round(m.x + half); x++) {
    const u = (x - m.x) / half
    const a = Math.abs(u)
    const y = Math.round(line(x))
    // Upper lip: thin at the corners, two peaks either side of the middle, a dip between.
    const peak = Math.exp(-(((a - 0.3) / 0.18) ** 2)) * 1.4 - Math.exp(-((a / 0.1) ** 2)) * 0.6
    const top = Math.round(y - 1 - (1 - a) * 1.2 - peak)
    for (let k = top; k < y; k++) (k === top ? upperDark : upper).push([x, k])
    for (let k = 1; k <= openPx; k++) inside.push([x, y + k - 1])
    // Lower lip: fuller in the middle.
    const depth = Math.round((1 - a * a) * 3)
    for (let k = 1; k <= depth; k++) (k === 1 && a < 0.4 ? lowerLight : lower).push([x, y + openPx + k])
  }
  px(g, LIP[1], upper)
  px(g, LIP[0], upperDark.filter(([x]) => Math.abs(x - m.x) > 1))
  px(g, LIP[1], upperDark.filter(([x]) => Math.abs(x - m.x) <= 1))
  px(g, LIP[2], lower)
  px(g, '#eca0a0', lowerLight)
  if (openPx) {
    px(g, '#3a1418', inside)
    if (openPx > 1) px(g, '#e8e0dc', inside.filter(([, y]) => y === Math.round(line(m.x))).filter(([x]) => Math.abs(x - m.x) < half * 0.6))
  }
  px(g, '#6a2832', curve(line, m.x - half, m.x + half))
  // Corners: a dimple of shadow, which turns up with a smile and down with a pout.
  px(g, SHADOW, [
    [Math.round(m.x - half - 1), Math.round(line(m.x - half) - Math.sign(e.smile))],
    [Math.round(m.x + half + 1), Math.round(line(m.x + half) - Math.sign(e.smile))],
  ])
  // The soft shadow under the lower lip.
  px(g, SHADOW, [
    [m.x - 2, m.y + 5 + openPx],
    [m.x - 1, m.y + 5 + openPx],
    [m.x, m.y + 5 + openPx],
    [m.x + 1, m.y + 5 + openPx],
  ])
}

function frontHair(g: CanvasRenderingContext2D, e: Expression, t: number) {
  const turn = e.turn * 4
  const sway = Math.sin(t * 0.9 + 0.6) * 1.2
  g.save()
  g.translate(turn, 0)
  const grad = g.createLinearGradient(40, 22, 90, 140)
  grad.addColorStop(0, '#343a58')
  grad.addColorStop(0.3, '#1c2034')
  grad.addColorStop(1, '#0c0c16')
  g.fillStyle = grad
  // Bangs swept from a part on her right across the forehead, ending in points.
  g.beginPath()
  g.moveTo(55, 25)
  g.bezierCurveTo(68, 27, 82, 33, 88, 48)
  g.lineTo(86, 54)
  g.lineTo(82, 49)
  g.lineTo(79, 53)
  g.lineTo(75, 47)
  g.lineTo(70, 50)
  g.lineTo(66, 45)
  g.lineTo(61, 47)
  g.lineTo(57, 43)
  g.lineTo(52, 46)
  g.bezierCurveTo(48, 46, 45, 50, 44, 56)
  g.bezierCurveTo(41, 44, 44, 31, 55, 25)
  g.fill()
  // Two locks from the temples, in front of the ears, down past the collarbones.
  for (const side of [-1, 1]) {
    const x0 = 64 + side * 21
    const sw = sway * (side > 0 ? 1 : 0.8)
    g.beginPath()
    g.moveTo(x0 - 2, 44)
    g.bezierCurveTo(x0 + side * 3, 62, x0 + side * 1, 86, x0 + side * 3 + sw, 112)
    g.lineTo(x0 + side * 2 + sw, 124)
    g.lineTo(x0 + side * 5 + sw, 116)
    g.bezierCurveTo(x0 + side * 6, 90, x0 + side * 6, 64, x0 + 3, 44)
    g.closePath()
    g.fill()
  }
  g.restore()
}

function strands(g: CanvasRenderingContext2D, e: Expression, t: number) {
  // One lighter strand down each lock, and a couple sweeping with the bangs.
  const turn = e.turn * 4
  const sway = Math.sin(t * 0.9 + 0.6) * 1.2
  const pts: P[] = []
  for (const side of [-1, 1]) {
    const x0 = 64 + side * 21
    for (let y = 50; y < 116; y++) {
      if (Math.floor(y / 8) % 3 === 2) continue
      const k = (y - 50) / 66
      pts.push([x0 + side * (1.5 + Math.sin(k * Math.PI) * 1.5) + sway * k + turn, y])
    }
  }
  for (let x = 58; x < 84; x++) {
    const k = (x - 58) / 26
    if (x % 6 === 0) continue
    pts.push([x + turn, 29 + k * 11 + k * k * 4])
  }
  px(g, '#343a58', pts)
}

function pin(g: CanvasRenderingContext2D, e: Expression, t: number) {
  // A hairpin through the bun, and a bookmark ribbon hanging from its end with a tassel.
  const turn = e.turn * 1.5
  const swing = Math.sin(t * 1.3) * 1.2
  px(g, '#e0b050', curve((x) => 6 + (x - 46) * 0.36, 46 + turn, 88 + turn))
  px(g, '#a8782a', curve((x) => 7 + (x - 46) * 0.36, 48 + turn, 87 + turn))
  px(g, '#f8e0a0', [
    [45 + turn, 6],
    [46 + turn, 5],
    [44 + turn, 7],
  ])
  const top: P = [88 + turn, 22]
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
  layer(g, RAMPS.hair, (c) => backHair(c, e))
  hairSheen(g, e)
  layer(g, RAMPS.skin, body)
  layer(g, RAMPS.cloth, clothes)
  chest(g)
  layer(g, RAMPS.obi, obi)
  pattern(g)
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
