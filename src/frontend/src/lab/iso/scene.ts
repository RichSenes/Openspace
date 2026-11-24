import { makeBrain, type Brain } from './brain'
import { glow, benchDesk, capsule, chair, coffeeCounter, cornerDesk, glassWall, holoTable, microscopeBench, mirrored, plant, rack, sofa, totem, whiteboard, type Prop } from './props'
import { CENTER, makeRoom, screenOf, SCENE_H, SCENE_W, type Room } from './room'
import { FOOT_X, FOOT_Y, facingOf, scientistFrames, type Facing, type Frames, type Look, type Pose } from './scientist'

/**
 * The lab, alive: furniture placed on the floor, Shiori in the middle, scientists going about
 * their day. Everything is sorted back to front each frame by where it stands.
 */

interface Placed {
  prop: Prop
  a: number
  b: number
  flip: number
}

export type Step = { go: [number, number]; first?: 'a' | 'b' } | { act: Pose; face: Facing; secs: number; bubble?: Bubble }
export type Bubble = 'question' | 'idea' | 'chat' | 'dots'

export interface Person {
  look: Look
  frames: Frames
  a: number
  b: number
  facing: Facing
  pose: Pose
  route: Step[]
  step: number
  /** Seconds into the current step. */
  into: number
  bubble?: Bubble
  seed: number
}

/** How fast they walk, in floor units a second. */
export const SPEED = 15

/**
 * Moves someone along their route: walking goes along one floor axis then the other, as on a
 * grid; an act holds a pose for its seconds. Returns nothing: it changes the person.
 */
export function advance(p: Person, dt: number) {
  let left = dt
  for (let guard = 0; guard < 8 && left > 0; guard++) {
    const step = p.route[p.step]
    if ('go' in step) {
      const [ta, tb] = step.go
      const alongA = (step.first ?? 'a') === 'a' ? p.a !== ta : p.b === tb
      const da = alongA ? ta - p.a : 0
      const db = alongA ? 0 : tb - p.b
      const dist = Math.abs(da) + Math.abs(db)
      if (dist < 1e-6) {
        p.step = (p.step + 1) % p.route.length
        p.into = 0
        continue
      }
      p.pose = 'walk'
      p.bubble = undefined
      p.facing = facingOf(da, db)
      const move = Math.min(dist, left * SPEED)
      p.a += Math.sign(da) * (da ? move : 0)
      p.b += Math.sign(db) * (db ? move : 0)
      p.into += move / SPEED
      left -= move / SPEED
    } else {
      p.pose = step.act
      p.facing = step.face
      // A question shows first, then the act; other bubbles stay.
      p.bubble = step.bubble === 'question' ? (p.into < 1.6 ? 'question' : undefined) : step.bubble
      const need = step.secs - p.into
      if (left < need) {
        p.into += left
        left = 0
      } else {
        left -= need
        p.step = (p.step + 1) % p.route.length
        p.into = 0
      }
    }
  }
}

const GREEN = '#3fd29a'
const BLUE = '#5b9bff'
const AMBER = '#f2a948'
const VIOLET = '#c48bff'

function layout(): Placed[] {
  const at = (prop: Prop, a: number, b: number, flip = false): Placed => ({
    prop: flip ? mirrored(prop) : prop,
    a,
    b,
    flip: flip ? -1 : 1,
  })
  return [
    // Research: capsules against the wall, a bench, a board.
    at(capsule('#37d6a0', 3), 4, 24),
    at(capsule('#4cc3ff', 8), 4, 40),
    at(capsule('#b98cff', 12), 4, 56),
    at(microscopeBench(GREEN), 24, 30, true),
    at(whiteboard('math', 5), 6, 84),
    // Engineering: racks, the long bench, a hologram table.
    at(rack(4), 16, 3),
    at(rack(9), 28, 3),
    at(rack(14), 40, 3),
    at(benchDesk(BLUE, 1), 54, 12),
    at(chair(BLUE), 59, 28),
    at(chair(BLUE), 72, 28),
    at(chair(BLUE), 85, 28),
    at(holoTable(BLUE), 110, 12),
    at(glassWall(26), 112, 46),
    // Data: the corner desk, a diagram, a live totem.
    at(cornerDesk(AMBER, 2), 12, 104),
    at(whiteboard('diagram', 7), 52, 128),
    at(totem(AMBER), 4, 110),
    // Lounge.
    at(coffeeCounter(VIOLET), 112, 86),
    at(sofa('#6c7fa8'), 108, 124),
    at(whiteboard('graph', 9), 112, 62),
    at(plant(6), 136, 130),
    at(plant(11), 92, 138),
    at(plant(3), 4, 6),
  ]
}

/**
 * Everyone keeps a few steps from the dome: nobody comes closer to her than this, in floor units.
 */
export const KEEP_AWAY = 36

const PEOPLE: { look: Look; route: Step[]; start: [number, number] }[] = [
  {
    // Engineering: types, then goes to ask Shiori something and takes notes.
    look: { skin: '#f1c9a5', hair: '#1d1a22', style: 'short', shirt: BLUE, glasses: true },
    start: [66, 33],
    route: [
      { act: 'type', face: 'NE', secs: 6 },
      { go: [66, 35], first: 'b' },
      { go: [110, 35] },
      { go: [110, 66], first: 'b' },
      { act: 'notes', face: 'NW', secs: 6, bubble: 'question' },
      { act: 'think', face: 'NW', secs: 2.5, bubble: 'dots' },
      { go: [110, 35], first: 'b' },
      { go: [66, 35] },
      { go: [66, 33], first: 'b' },
    ],
  },
  {
    // Research: at the bench, then checks a capsule.
    look: { skin: '#e8b48f', hair: '#5a3424', style: 'long', shirt: GREEN },
    start: [42, 44],
    route: [
      { act: 'type', face: 'NW', secs: 5 },
      { go: [42, 26], first: 'b' },
      { go: [20, 26] },
      { go: [20, 30], first: 'b' },
      { act: 'notes', face: 'NW', secs: 5 },
      { act: 'think', face: 'NW', secs: 3, bubble: 'idea' },
      { go: [20, 26], first: 'b' },
      { go: [42, 26] },
      { go: [42, 44], first: 'b' },
    ],
  },
  {
    // Data: at the desk, to the diagram, then a coffee with a colleague.
    look: { skin: '#8d5a3b', hair: '#141018', style: 'curly', shirt: AMBER },
    start: [30, 124],
    route: [
      { act: 'type', face: 'NE', secs: 6 },
      { go: [30, 136], first: 'b' },
      { go: [66, 136] },
      { act: 'think', face: 'NE', secs: 3, bubble: 'dots' },
      { act: 'notes', face: 'NE', secs: 3 },
      { go: [100, 136] },
      { go: [100, 100], first: 'b' },
      { go: [118, 100] },
      { act: 'coffee', face: 'SE', secs: 7, bubble: 'chat' },
      { go: [100, 100] },
      { go: [100, 136], first: 'b' },
      { go: [30, 136] },
      { go: [30, 124], first: 'b' },
    ],
  },
  {
    // Engineering: at the racks, looks at the wall screens, coffee, back.
    look: { skin: '#f6d7c3', hair: '#c9a06a', style: 'bun', shirt: BLUE },
    start: [33, 16],
    route: [
      { act: 'type', face: 'NE', secs: 5 },
      { go: [33, 36], first: 'b' },
      { go: [108, 36] },
      { act: 'think', face: 'NE', secs: 3, bubble: 'idea' },
      { go: [110, 36] },
      { go: [110, 100], first: 'b' },
      { go: [128, 100] },
      { act: 'coffee', face: 'NW', secs: 6, bubble: 'chat' },
      { go: [110, 100] },
      { go: [110, 36], first: 'b' },
      { go: [33, 36] },
      { go: [33, 16], first: 'b' },
    ],
  },
  {
    // Research: goes to Shiori with a question, then back to the board.
    look: { skin: '#c98e66', hair: '#2a2a33', style: 'pony', shirt: GREEN },
    start: [22, 94],
    route: [
      { act: 'notes', face: 'NE', secs: 4 },
      { go: [22, 100], first: 'b' },
      { go: [46, 100] },
      { go: [46, 106], first: 'b' },
      { act: 'notes', face: 'NE', secs: 6, bubble: 'question' },
      { act: 'think', face: 'NE', secs: 3, bubble: 'idea' },
      { go: [46, 100], first: 'b' },
      { go: [22, 100] },
      { go: [22, 94], first: 'b' },
    ],
  },
]

export function makePeople(): Person[] {
  return PEOPLE.map((p, i) => ({
    look: p.look,
    frames: scientistFrames(p.look),
    a: p.start[0],
    b: p.start[1],
    facing: 'SE',
    pose: 'stand',
    route: p.route,
    step: 0,
    into: i * 0.7,
    seed: i,
  }))
}

/** A bubble over someone's head: a question, an idea, a chat, or thinking dots. */
export function drawBubble(ctx: CanvasRenderingContext2D, x: number, y: number, kind: Bubble, t: number, seed: number) {
  const px = (dx: number, dy: number, c: string) => {
    ctx.fillStyle = c
    ctx.fillRect(Math.round(x + dx), Math.round(y + dy), 1, 1)
  }
  const w = 13
  const h = 9
  const x0 = -6
  const y0 = -12
  ctx.fillStyle = '#18202b'
  ctx.fillRect(Math.round(x + x0), Math.round(y + y0 - 1), w, h + 2)
  ctx.fillRect(Math.round(x + x0 - 1), Math.round(y + y0), w + 2, h)
  ctx.fillStyle = '#f7fbff'
  ctx.fillRect(Math.round(x + x0), Math.round(y + y0), w, h)
  px(-1, y0 + h + 1, '#f7fbff')
  px(-2, y0 + h + 2, '#f7fbff')
  px(0, y0 + h + 1, '#18202b')
  px(-1, y0 + h + 2, '#18202b')
  px(-3, y0 + h + 2, '#18202b')
  px(-2, y0 + h + 3, '#18202b')
  if (kind === 'question') {
    for (const [dx, dy] of [
      [-1, -10],
      [0, -11],
      [1, -11],
      [2, -10],
      [2, -9],
      [1, -8],
      [0, -7],
      [0, -5],
    ])
      px(dx, dy, '#c23b4a')
  } else if (kind === 'idea') {
    const on = Math.floor(t * 3 + seed) % 2 === 0
    for (const [dx, dy] of [
      [-1, -11],
      [0, -11],
      [1, -11],
      [-2, -10],
      [2, -10],
      [-2, -9],
      [2, -9],
      [-1, -8],
      [1, -8],
    ])
      px(dx, dy, on ? '#f2b630' : '#c9a040')
    px(0, -9, on ? '#fff1a8' : '#e8d58a')
    px(-1, -7, '#5d6878')
    px(0, -7, '#5d6878')
    px(1, -7, '#5d6878')
  } else if (kind === 'chat') {
    // Squiggles: talk, not words.
    for (let k = 0; k < 9; k++) px(-4 + k, -9 + Math.round(Math.sin(k * 1.4 + t * 6 + seed) * 1), '#5b6fa8')
    for (let k = 0; k < 5; k++) px(-4 + k, -6, '#8d99a8')
  } else {
    const n = Math.floor(t * 3) % 4
    for (let k = 0; k < 3; k++) if (k < n) px(-3 + k * 3, -8, '#3a4352')
  }
}

export interface Footprint {
  a0: number
  b0: number
  a1: number
  b1: number
}

interface Drawable extends Footprint {
  /** What it covers on screen: only things that overlap there need an order. */
  box: [number, number, number, number]
  draw: (ctx: CanvasRenderingContext2D, t: number) => void
}

/**
 * Back to front: two things that overlap on screen are ordered by their footprints (one is
 * wholly behind the other along a floor axis), then everything is drawn in an order that keeps
 * all those pairs, as iso engines do. A plain sort cannot: the relation is not transitive.
 */
export function order<T extends Footprint & { box: [number, number, number, number] }>(items: T[]): T[] {
  const n = items.length
  const after: number[][] = items.map(() => [])
  const waiting = new Array<number>(n).fill(0)
  for (let i = 0; i < n; i++) {
    for (let j = i + 1; j < n; j++) {
      const x = items[i].box
      const y = items[j].box
      if (x[2] <= y[0] || y[2] <= x[0] || x[3] <= y[1] || y[3] <= x[1]) continue
      const k = behind(items[i], items[j])
      const [first, second] = k <= 0 ? [i, j] : [j, i]
      after[first].push(second)
      waiting[second]++
    }
  }
  const depth = (k: number) => items[k].a0 + items[k].a1 + items[k].b0 + items[k].b1
  const ready = items.map((_, k) => k).filter((k) => waiting[k] === 0)
  const out: T[] = []
  const done = new Array<boolean>(n).fill(false)
  while (out.length < n) {
    if (!ready.length) {
      // A loop: break it at the farthest thing left.
      let pick = -1
      for (let k = 0; k < n; k++) if (!done[k] && (pick < 0 || depth(k) < depth(pick))) pick = k
      ready.push(pick)
      waiting[pick] = 0
    }
    ready.sort((p, q) => depth(q) - depth(p))
    const k = ready.pop()!
    if (done[k]) continue
    done[k] = true
    out.push(items[k])
    for (const m of after[k]) if (--waiting[m] === 0 && !done[m]) ready.push(m)
  }
  return out
}

/** Whether x stands behind y: compared by footprints, as iso games do. */
export function behind(x: { a0: number; b0: number; a1: number; b1: number }, y: { a0: number; b0: number; a1: number; b1: number }): number {
  if (x.a1 <= y.a0 + 0.01) return -1
  if (y.a1 <= x.a0 + 0.01) return 1
  if (x.b1 <= y.b0 + 0.01) return -1
  if (y.b1 <= x.b0 + 0.01) return 1
  return x.a0 + x.a1 + x.b0 + x.b1 - (y.a0 + y.a1 + y.b0 + y.b1)
}

export interface Scene {
  room: Room
  brain: Brain
  props: Placed[]
  people: Person[]
  draw(ctx: CanvasRenderingContext2D, t: number, dt: number, night?: boolean): void
}

export function makeScene(): Scene {
  const room = makeRoom()
  const brain = makeBrain()
  const props = layout()
  const people = makePeople()
  const DOME = 34
  return {
    room,
    brain,
    props,
    people,
    draw(ctx, t, dt, night = false) {
      for (const p of people) advance(p, dt)
      ctx.imageSmoothingEnabled = false
      ctx.fillStyle = '#0d131c'
      ctx.fillRect(0, 0, SCENE_W, SCENE_H)
      ctx.drawImage(room.canvas, 0, 0)
      room.live(ctx, t)
      const items: Drawable[] = props.map((p) => {
        const [x, y] = screenOf(p.a, p.b)
        const sx = x - p.prop.sprite.ax
        const sy = y - p.prop.sprite.ay
        return {
          a0: p.a,
          b0: p.b,
          a1: p.a + p.prop.da,
          b1: p.b + p.prop.db,
          box: [sx, sy, sx + p.prop.sprite.canvas.width, sy + p.prop.sprite.canvas.height] as [number, number, number, number],
          draw: (c: CanvasRenderingContext2D, tt: number) => {
            c.drawImage(p.prop.sprite.canvas, x - p.prop.sprite.ax, y - p.prop.sprite.ay)
            p.prop.anim?.(c, x, y, tt, p.flip)
          },
        }
      })
      const [bx, by] = screenOf(CENTER[0], CENTER[1])
      items.push({
        a0: CENTER[0] - DOME,
        b0: CENTER[1] - DOME,
        a1: CENTER[0] + DOME,
        b1: CENTER[1] + DOME,
        box: [bx - 110, by - 110, bx + 110, by + 80],
        draw: (c, tt) => brain.draw(c, bx, by, tt),
      })
      for (const p of people) {
        const [px, py] = screenOf(p.a, p.b)
        items.push({
          a0: p.a - 2,
          b0: p.b - 2,
          a1: p.a + 2,
          b1: p.b + 2,
          box: [px - 20, py - 70, px + 20, py + 4],
          draw: (c, tt) => {
            const [x, y] = [px, py]
            const frame = p.pose === 'walk' ? Math.floor(p.into * 8) : p.pose === 'stand' || p.pose === 'think' ? 0 : Math.floor(tt * (p.pose === 'coffee' ? 0.5 : 3) + p.seed)
            const sprite = p.frames.get(p.facing, p.pose, frame)
            c.globalAlpha = 0.22
            c.fillStyle = '#0b1220'
            c.fillRect(Math.round(x - 6), Math.round(y - 1), 13, 3)
            c.fillRect(Math.round(x - 4), Math.round(y - 2), 9, 5)
            c.globalAlpha = 1
            c.drawImage(sprite, Math.round(x - FOOT_X), Math.round(y - FOOT_Y))
            if (p.bubble) drawBubble(c, x + 6, y - 50, p.bubble, tt, p.seed)
          },
        })
      }
      for (const item of order(items)) item.draw(ctx, t)
      if (night) {
        // The lights go down: what glows is what lights the room.
        ctx.save()
        ctx.globalCompositeOperation = 'multiply'
        ctx.fillStyle = '#39436a'
        ctx.fillRect(0, 0, SCENE_W, SCENE_H)
        ctx.restore()
        for (const p of props) {
          const [x, y] = screenOf(p.a, p.b)
          for (const l of p.prop.lights ?? []) glow(ctx, x + p.flip * l.dx, y + l.dy, l.r, l.color, 0.55)
        }
        const [cx, cy] = screenOf(CENTER[0], CENTER[1])
        glow(ctx, cx, cy - 30, 150, '#b25cff', 0.45 + 0.1 * Math.sin(t * 1.7))
        glow(ctx, cx, cy, 120, '#46ffb0', 0.7)
        room.glows(ctx, t)
      }
    },
  }
}
