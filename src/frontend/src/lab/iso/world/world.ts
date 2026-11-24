import { makeBrain, REFERENCE, type Brain } from '../brain'
import { SQUASH } from '../iso'
import { mix, rand } from '../px'
import { drawBubble, order, type Bubble } from '../scene'
import { FOOT_X, FOOT_Y, facingOf, scientistFrames, type Facing, type Frames, type HairStyle, type Look, type Pose } from '../scientist'
import { buildWorld, T, tint, type World } from './build'
import { findPath, type Spot } from './furnish'
import { hashOf, type OfficeSpec } from './layout'
import { label } from './decor'
import type { CharacterKind, CharacterState, WorldView } from '../../../api/types'
import { intentOf, lookOf, queueOf } from './intent'
import { cityBurns, groundLight, isDark, skyColour, stormAt, type Weather } from './sky'

/**
 * The lab alive. Each frame: everything is drawn back to front as it looks under white light;
 * a light map is painted (daylight outside by the hour and the weather, cold lamps inside, pools
 * of light thrown on the floor by screens, capsules, Shiori's well, street lamps at night) and
 * the picture is multiplied by it; then whatever gives light (screens, liquids, windows, lamps)
 * is laid over unlit, with a soft bloom. Things in front hide the light of things behind them.
 */

export interface Conditions {
  hour: number
  weather: Weather
  /** The lab's lamps: on, or off (only what glows lights the room). */
  lamps: boolean
}

interface Agent {
  look: Look
  frames: Frames
  a: number
  b: number
  facing: Facing
  pose: Pose
  path: [number, number][]
  /** What they do once there: for `secs` from arrival (`since`). */
  act: { pose: Pose; face: Facing; secs: number; since: number; bubble?: Bubble } | null
  seat: Spot
  room: number
  seed: number
  bubble?: Bubble
  walked: number
  /** A real character (desk, routine, intern) or one of the demo's. */
  kind: CharacterKind | 'Demo'
  id?: string
  state?: CharacterState
  /** What it brings to Shiori, when it queues. */
  need?: string
  /** Not in the lab: a routine switched off. */
  away?: boolean
}

const SPEED = 15
const SKINS = ['#f1c9a5', '#e8b48f', '#8d5a3b', '#f6d7c3', '#c98e66', '#5e3b28', '#f3d1b5']
const HAIRS = ['#1d1a22', '#5a3424', '#141018', '#c9a06a', '#2a2a33', '#8a4a2a', '#d8d0c0']
const STYLES: HairStyle[] = ['short', 'long', 'curly', 'bun', 'pony']

export interface Lab {
  world: World
  /** The real world changed: characters take their new places, interns come and go. */
  update(view: WorldView): void
  /** Paints the view: a window of `viewW` × `viewH` world pixels, the lab in its middle. */
  draw(ctx: CanvasRenderingContext2D, t: number, dt: number, when: Conditions, viewW: number, viewH: number, centre?: [number, number]): void
}

/**
 * The world is painted once, wide enough for a very large screen; changing the size of the page
 * only changes the window onto it, always centred on the lab.
 */
export const WORLD_W = 2880
export const WORLD_H = 1800

export function makeLab(offices: OfficeSpec[], live?: WorldView): Lab {
  const world = buildWorld(offices, WORLD_W, WORLD_H)
  const { plan, furnished, screen, width, height } = world
  const brain: Brain = makeBrain(REFERENCE)
  const r = rand(Math.floor(hashOf(offices.map((o) => o.name).join(), 8) * 1e6))
  const seats = furnished.spots.filter((s) => s.use === 'seat')
  const agents: Agent[] = live
    ? realAgents(live)
    : seats.map((seat, i) => {
        const room = plan.spaces[seat.room]
        const look: Look = {
          skin: SKINS[Math.floor(r() * SKINS.length)],
          hair: HAIRS[Math.floor(r() * HAIRS.length)],
          style: STYLES[Math.floor(r() * STYLES.length)],
          shirt: room.accent,
          glasses: r() > 0.65,
        }
        return {
          look,
          frames: scientistFrames(look),
          a: seat.a,
          b: seat.b,
          facing: seat.face,
          pose: 'type',
          path: [],
          act: { pose: 'type', face: seat.face, secs: 2 + r() * 12, since: 0 },
          seat,
          room: seat.room,
          seed: i,
          walked: 0,
          kind: 'Demo' as const,
        }
      })

  /** The real characters, each at a seat of its office's area: desks first, then routines. */
  function realAgents(view: WorldView): Agent[] {
    const out: Agent[] = []
    for (const space of plan.spaces.filter((s) => s.kind === 'office')) {
      const office = view.offices.find((o) => o.name === space.name)
      if (!office) continue
      const mine = seats.filter((s) => s.room === space.id)
      const people = [...office.characters.filter((c) => c.kind === 'Desk'), ...office.characters.filter((c) => c.kind === 'Routine')]
      people.forEach((c, i) => {
        const seat = mine[i]
        if (!seat) return
        const look = lookOf(c.id, c.kind, space.accent)
        out.push({
          look,
          frames: scientistFrames(look),
          a: seat.a,
          b: seat.b,
          facing: seat.face,
          pose: 'stand',
          path: [],
          act: null,
          seat,
          room: space.id,
          seed: out.length,
          walked: 0,
          kind: c.kind,
          id: c.id,
          state: c.state,
        })
      })
    }
    return out
  }

  // Where they queue at Shiori: round her dome, the front first.
  const queueSpots = furnished.spots.filter((s) => s.use === 'shiori').sort((x, y) => y.a + y.b - (x.a + x.b))
  const queued = new Map<string, number>()

  // Each office's name, floating over its far corner: easier than telling colours apart.
  const names = plan.spaces.filter((s) => s.kind === 'office' || s.kind === 'lounge').map((s) => ({ canvas: label(s.name, s.accent), at: screen((s.a0 + 1.5) * T, (s.b0 + 1.5) * T, 46) }))
  const tileOf = (a: number, b: number): [number, number] => [Math.floor(a / T), Math.floor(b / T)]
  const goTo = (ag: Agent, spot: Spot, act: Agent['act']) => {
    const path = findPath(plan, furnished.blocked, tileOf(ag.a, ag.b), tileOf(spot.a, spot.b))
    if (!path) return false
    ag.path = [...path.slice(1, -1).map(([a, b]) => [a * T + 4, b * T + 4] as [number, number]), [spot.a, spot.b]]
    // Along the axes only: from the last tile centre to the spot, one axis at a time.
    const last = path.length > 1 ? path[path.length - 2] : tileOf(ag.a, ag.b)
    ag.path.splice(ag.path.length - 1, 0, [spot.a, last[1] * T + 4])
    ag.act = act
    ag.bubble = undefined
    return true
  }
  const pick = (use: Spot['use'], room?: number) => {
    const list = furnished.spots.filter((s) => s.use === use && (room === undefined || s.room === room))
    return list.length ? list[Math.floor(r() * list.length)] : null
  }

  const decide = (ag: Agent, t: number) => {
    if (ag.kind !== 'Demo') {
      decideReal(ag, t)
      return
    }
    const atSeat = Math.hypot(ag.a - ag.seat.a, ag.b - ag.seat.b) < 1
    if (!atSeat) {
      goTo(ag, ag.seat, { pose: 'type', face: ag.seat.face, secs: 10 + r() * 18, since: t })
      return
    }
    const roll = r()
    const spot = roll < 0.25 ? pick('shiori') : roll < 0.45 ? pick('coffee') : roll < 0.6 ? pick('board', ag.room) : null
    if (spot && spot.use === 'shiori' && goTo(ag, spot, { pose: 'notes', face: spot.face, secs: 6 + r() * 4, since: t, bubble: 'question' })) return
    if (spot && spot.use === 'coffee' && goTo(ag, spot, { pose: 'coffee', face: spot.face, secs: 6 + r() * 6, since: t, bubble: 'chat' })) return
    if (spot && spot.use === 'board' && goTo(ag, spot, { pose: 'think', face: spot.face, secs: 4 + r() * 4, since: t, bubble: r() > 0.5 ? 'idea' : 'dots' })) return
    ag.act = { pose: 'type', face: ag.seat.face, secs: 8 + r() * 14, since: t }
  }

  /** A real character: where its state puts it, and there it stays until the state changes. */
  const decideReal = (ag: Agent, t: number) => {
    const intent = intentOf(ag.state ?? 'Free', ag.need)
    ag.away = intent.where === 'away'
    const FOREVER = 1e9
    if (intent.where === 'away') {
      ag.act = { pose: 'stand', face: ag.seat.face, secs: FOREVER, since: t }
      return
    }
    if (intent.where === 'seat') {
      const atSeat = Math.hypot(ag.a - ag.seat.a, ag.b - ag.seat.b) < 1
      const act = { pose: intent.pose, face: ag.seat.face, secs: FOREVER, since: t }
      if (atSeat || !goTo(ag, ag.seat, act)) ag.act = act
      return
    }
    if (intent.where === 'queue') {
      const spot = queueSpots[(queued.get(ag.id ?? '') ?? 0) % Math.max(1, queueSpots.length)]
      const act = { pose: intent.pose, face: spot?.face ?? ag.facing, secs: FOREVER, since: t, bubble: intent.bubble }
      if (!spot || !goTo(ag, spot, act)) {
        ag.act = act
        ag.bubble = intent.bubble
      }
      return
    }
    // Free: the lounge, the coffee, the boards, or a moment at the desk doing nothing much.
    const roll = r()
    const spot = roll < 0.35 ? pick('coffee') : roll < 0.55 ? pick('board', ag.room) : roll < 0.7 ? pick('sofa') : null
    if (spot && spot.use === 'coffee' && goTo(ag, spot, { pose: 'coffee', face: spot.face, secs: 8 + r() * 8, since: t, bubble: 'chat' })) return
    if (spot && spot.use === 'board' && goTo(ag, spot, { pose: 'think', face: spot.face, secs: 5 + r() * 5, since: t, bubble: r() > 0.5 ? 'idea' : 'dots' })) return
    if (spot && spot.use === 'sofa' && goTo(ag, spot, { pose: 'stand', face: spot.face, secs: 8 + r() * 10, since: t, bubble: 'chat' })) return
    const atSeat = Math.hypot(ag.a - ag.seat.a, ag.b - ag.seat.b) < 1
    if (!atSeat && goTo(ag, ag.seat, { pose: 'stand', face: ag.seat.face, secs: 6 + r() * 10, since: t })) return
    ag.act = { pose: r() > 0.5 ? 'think' : 'stand', face: ag.seat.face, secs: 6 + r() * 10, since: t }
  }

  /** Starts again from where they are: their state changed. */
  const rethink = (ag: Agent) => {
    // Finish the step to the next tile centre, so they never stop between two tiles.
    ag.path = ag.path.slice(0, 1)
    ag.act = null
    ag.bubble = undefined
  }

  const update = (view: WorldView) => {
    // Characters met for the first time take their seats.
    for (const a of realAgents(view)) if (!agents.some((x) => x.id === a.id)) agents.push({ ...a, seed: agents.length })
    const byId = new Map(view.offices.flatMap((o) => o.characters.map((c) => [c.id, c] as const)))
    const present = new Set(agents.filter((a) => a.id).map((a) => a.id!))
    const line = queueOf(view, present)
    const needs = new Map(line.map((q) => [q.id, q.need]))
    const before = new Map(queued)
    queued.clear()
    line.forEach((q, i) => queued.set(q.id, i))
    for (const ag of agents) {
      if (!ag.id) continue
      const c = byId.get(ag.id)
      if (!c) continue
      const changed = c.state !== ag.state || needs.get(ag.id) !== ag.need || before.get(ag.id) !== queued.get(ag.id)
      ag.state = c.state
      ag.need = needs.get(ag.id)
      if (changed) rethink(ag)
    }
    // Interns: they come in beside the desk they help, and leave when it is done.
    const interns = view.offices.flatMap((o) => o.characters.filter((c) => c.kind === 'Intern'))
    const wanted = new Set(interns.map((c) => c.id))
    for (let i = agents.length - 1; i >= 0; i--) if (agents[i].kind === 'Intern' && !wanted.has(agents[i].id!)) agents.splice(i, 1)
    for (const c of interns) {
      if (agents.some((a) => a.id === c.id)) continue
      const desk = agents.find((a) => a.id === c.forDesk)
      if (!desk) continue
      const beside = besideOf(desk.seat)
      if (!beside) continue
      const look = lookOf(c.id, 'Intern', plan.spaces[desk.room].accent)
      // In from the walkway by Shiori, then to the desk.
      const start = queueSpots[agents.length % Math.max(1, queueSpots.length)] ?? beside
      agents.push({
        look,
        frames: scientistFrames(look),
        a: start.a,
        b: start.b,
        facing: 'SE',
        pose: 'walk',
        path: [],
        act: null,
        seat: beside,
        room: desk.room,
        seed: agents.length,
        walked: 0,
        kind: 'Intern',
        id: c.id,
        state: 'Working',
      })
    }
  }

  /** A free tile next to a seat, where an intern stands to help. */
  const besideOf = (seat: Spot): Spot | null => {
    const [ta, tb] = tileOf(seat.a, seat.b)
    for (const [da, db] of [
      [1, 0],
      [-1, 0],
      [0, 1],
      [1, 1],
      [-1, 1],
    ]) {
      const a = ta + da
      const b = tb + db
      if (a < 0 || b < 0 || a >= plan.w || b >= plan.h || plan.cells[b * plan.w + a] < 0 || furnished.blocked[b * plan.w + a]) continue
      if (agents.some((x) => tileOf(x.seat.a, x.seat.b).join() === `${a},${b}`)) continue
      return { ...seat, a: a * T + 4, b: b * T + 4 }
    }
    return null
  }
  if (live) update(live)

  const step = (ag: Agent, t: number, dt: number) => {
    if (ag.path.length) {
      let left = dt * SPEED
      while (left > 0 && ag.path.length) {
        const [ta, tb] = ag.path[0]
        const da = ta - ag.a
        const db = tb - ag.b
        // One axis at a time, as on a grid.
        const moveA = Math.abs(da) > 1e-3
        const d = moveA ? da : db
        const dist = Math.abs(d)
        if (dist < 1e-3) {
          ag.path.shift()
          continue
        }
        ag.facing = facingOf(moveA ? da : 0, moveA ? 0 : db)
        const m = Math.min(dist, left)
        if (moveA) ag.a += Math.sign(d) * m
        else ag.b += Math.sign(d) * m
        ag.walked += m / SPEED
        left -= m
      }
      ag.pose = 'walk'
      if (!ag.path.length && ag.act) {
        // Arrived: the act's seconds start now.
        ag.act = { ...ag.act, since: t }
        ag.bubble = ag.act.bubble
      }
      return
    }
    if (ag.act && t < ag.act.since + ag.act.secs) {
      ag.pose = ag.act.pose
      ag.facing = ag.act.face
      // In the demo a question shows as they arrive, then they listen and write; a real one stays.
      if (ag.act.bubble === 'question' && ag.kind === 'Demo') ag.bubble = t - ag.act.since < 1.8 ? 'question' : undefined
      return
    }
    ag.bubble = undefined
    decide(ag, t)
  }

  // Layers the size of the view, made again when it changes.
  let vw = 0
  let vh = 0
  const layer = () => document.createElement('canvas')
  const light = layer()
  const glow = layer()
  const rain = layer()
  const tinted = layer()
  const small = layer()
  const lctx = light.getContext('2d')!
  const gctx = glow.getContext('2d')!
  const rctx = rain.getContext('2d')!
  const tctx = tinted.getContext('2d')!
  const sctx = small.getContext('2d')!
  const fit = (w: number, h: number) => {
    if (w === vw && h === vh) return
    vw = w
    vh = h
    for (const c of [light, glow, rain, tinted]) {
      c.width = w
      c.height = h
    }
    small.width = Math.ceil(w / 4)
    small.height = Math.ceil(h / 4)
  }
  const pool = (ctx: CanvasRenderingContext2D, x: number, y: number, rad: number, colour: string, strength: number) => {
    ctx.save()
    ctx.translate(x, y)
    ctx.scale(1, SQUASH)
    const g = ctx.createRadialGradient(0, 0, 0, 0, 0, rad)
    g.addColorStop(0, colour)
    g.addColorStop(0.55, mix(colour, '#000000', 0.55))
    g.addColorStop(1, '#000000')
    ctx.globalAlpha = Math.min(1, strength)
    ctx.fillStyle = g
    ctx.beginPath()
    ctx.arc(0, 0, rad, 0, Math.PI * 2)
    ctx.fill()
    ctx.restore()
  }
  const drops = Array.from({ length: 520 }, () => ({ x: r(), y: r(), s: 0.6 + r() * 0.6 }))

  return {
    world,
    update,
    draw(ctx, t, dt, when, viewW, viewH, centre) {
      fit(viewW, viewH)
      // Where the view sits on the world: its middle on the lab's, or where it was moved to,
      // never past the world's edges.
      const [cx0, cy0] = centre ?? [width / 2, height / 2]
      const ox = vw >= width ? Math.round((width - vw) / 2) : Math.round(Math.min(width - vw, Math.max(0, cx0 - vw / 2)))
      const oy = vh >= height ? Math.round((height - vh) / 2) : Math.round(Math.min(height - vh, Math.max(0, cy0 - vh / 2)))
      const inWorld = (c: CanvasRenderingContext2D) => c.setTransform(1, 0, 0, 1, -ox, -oy)
      const plain = (c: CanvasRenderingContext2D) => c.setTransform(1, 0, 0, 1, 0, 0)
      for (const ag of agents) step(ag, t, dt)
      const storm = stormAt(t, when.weather)
      const sky = cityBurns(when.hour) ? mix(skyColour(when.hour, when.weather), '#ff5a2a', 0.45) : skyColour(when.hour, when.weather)
      const windowSky = storm.flash > 0 ? mix(sky, '#ffffff', storm.flash * 0.85) : sky

      type Drawable = {
        a0: number
        b0: number
        a1: number
        b1: number
        box: [number, number, number, number]
        draw: (c: CanvasRenderingContext2D) => void
        light?: (c: CanvasRenderingContext2D) => void
      }
      const items: Drawable[] = []
      for (const p of world.props) {
        const [x, y] = screen(p.a, p.b)
        const s = p.prop.sprite
        const sx = x - s.ax
        const sy = y - s.ay
        items.push({
          a0: p.a,
          b0: p.b,
          a1: p.a + p.prop.da,
          b1: p.b + p.prop.db,
          box: [sx, sy, sx + s.canvas.width, sy + s.canvas.height],
          draw: (c) => c.drawImage(s.canvas, sx, sy),
          light: (c) => {
            if (p.outdoor && !isDark(when.hour)) return
            if (s.glow) c.drawImage(s.glow, sx, sy)
            p.prop.anim?.(c, x, y, t, p.flip)
          },
        })
      }
      for (const w of world.walls) {
        const [x, y] = screen(w.a0, w.b0)
        const s = w.sprite
        const sx = x - s.ax
        const sy = y - s.ay
        items.push({
          a0: w.a0,
          b0: w.b0,
          a1: w.a1,
          b1: w.b1,
          box: [sx, sy, sx + s.canvas.width, sy + s.canvas.height],
          draw: (c) => c.drawImage(s.canvas, sx, sy),
          light: w.window && s.glow ? (c) => c.drawImage(tint(s.glow!, windowSky, `${s.canvas.width}x${s.canvas.height}${w.a1 - w.a0}`), sx, sy) : undefined,
        })
      }
      // The city's traffic: cars and people, moved on.
      const cars = world.city.move(t, dt)
      for (const c of cars) {
        const [x, y] = screen(c.a, c.b)
        const s = c.prop.sprite
        const sx = x - s.ax
        const sy = y - s.ay
        items.push({
          a0: c.a,
          b0: c.b,
          a1: c.a + c.prop.da,
          b1: c.b + c.prop.db,
          box: [sx, sy, sx + s.canvas.width, sy + s.canvas.height],
          draw: (cx2) => cx2.drawImage(s.canvas, sx, sy),
          light: (cx2) => {
            if (isDark(when.hour) && s.glow) cx2.drawImage(s.glow, sx, sy)
          },
        })
      }
      const [cx, cy] = screen(world.centre[0], world.centre[1])
      const R = 34
      items.push({
        a0: world.centre[0] - R,
        b0: world.centre[1] - R,
        a1: world.centre[0] + R,
        b1: world.centre[1] + R,
        box: [cx - 110, cy - 110, cx + 110, cy + 80],
        draw: (c) => brain.draw(c, cx, cy, t),
        light: (c) => brain.draw(c, cx, cy, t),
      })
      for (const ag of agents) {
        if (ag.away) continue
        const [x, y] = screen(ag.a, ag.b)
        const frame = ag.pose === 'walk' ? Math.floor(ag.walked * 8) : ag.pose === 'type' || ag.pose === 'notes' ? Math.floor(t * 3 + ag.seed) : Math.floor(t * 0.5 + ag.seed)
        const sprite = ag.frames.get(ag.facing, ag.pose, frame)
        const sx = Math.round(x - FOOT_X)
        const sy = Math.round(y - FOOT_Y)
        items.push({
          a0: ag.a - 2,
          b0: ag.b - 2,
          a1: ag.a + 2,
          b1: ag.b + 2,
          box: [sx, sy, sx + sprite.width, sy + sprite.height],
          draw: (c) => {
            c.globalAlpha = 0.22
            c.fillStyle = '#0b1220'
            c.fillRect(Math.round(x - 5), Math.round(y - 1), 11, 3)
            c.globalAlpha = 1
            c.drawImage(sprite, sx, sy)
          },
        })
      }
      // Only what is on screen.
      const sorted = order(items.filter((it) => it.box[2] > ox && it.box[0] < ox + vw && it.box[3] > oy && it.box[1] < oy + vh))

      // Under white light.
      ctx.imageSmoothingEnabled = false
      ctx.globalCompositeOperation = 'source-over'
      plain(ctx)
      ctx.fillStyle = '#0d131c'
      ctx.fillRect(0, 0, vw, vh)
      ctx.drawImage(world.ground, ox, oy, vw, vh, 0, 0, vw, vh)
      inWorld(ctx)
      for (const it of sorted) it.draw(ctx)

      // What gives light, each hidden by whatever stands in front of it.
      gctx.globalCompositeOperation = 'source-over'
      plain(gctx)
      gctx.clearRect(0, 0, vw, vh)
      inWorld(gctx)
      for (const it of sorted) {
        gctx.globalCompositeOperation = 'destination-out'
        it.draw(gctx)
        gctx.globalCompositeOperation = 'source-over'
        it.light?.(gctx)
      }

      // The light map.
      const dark = isDark(when.hour)
      const outdoor = storm.flash > 0 ? mix(groundLight(when.hour, when.weather), '#ffffff', storm.flash * 0.7) : groundLight(when.hour, when.weather)
      const lampsOn = when.lamps && !storm.lampsDown
      const inside = lampsOn ? '#737c8f' : storm.lampsDown && when.lamps ? '#2a3044' : '#161b2b'
      plain(lctx)
      lctx.globalCompositeOperation = 'source-over'
      lctx.fillStyle = outdoor
      lctx.fillRect(0, 0, vw, vh)
      // Indoors, the lamps' colour: the building's mask, tinted.
      tctx.globalCompositeOperation = 'source-over'
      tctx.clearRect(0, 0, vw, vh)
      tctx.drawImage(world.indoor, ox, oy, vw, vh, 0, 0, vw, vh)
      tctx.globalCompositeOperation = 'source-in'
      tctx.fillStyle = inside
      tctx.fillRect(0, 0, vw, vh)
      lctx.drawImage(tinted, 0, 0)
      inWorld(lctx)
      lctx.globalCompositeOperation = 'lighter'
      for (const pl of world.pools) {
        if (pl.kind === 'ceiling' && !lampsOn) continue
        const [x, y] = screen(pl.a, pl.b)
        pool(lctx, x, y, pl.r * 2, pl.color, pl.strength)
      }
      for (const p of [...world.props, ...cars.map((c) => ({ ...c, outdoor: true }))]) {
        if (!p.prop.lights) continue
        if (p.outdoor && !dark) continue
        const [x, y] = screen(p.a, p.b)
        for (const l of p.prop.lights) pool(lctx, x + p.flip * l.dx, y + l.dy, l.r * 1.6, l.color, p.outdoor ? 0.9 : lampsOn ? 0.35 : 0.75)
      }

      plain(ctx)
      ctx.globalCompositeOperation = 'multiply'
      ctx.drawImage(light, 0, 0)
      ctx.globalCompositeOperation = 'source-over'
      ctx.drawImage(glow, 0, 0)
      // A soft bloom round the bright things, stronger in the dark.
      sctx.clearRect(0, 0, small.width, small.height)
      sctx.imageSmoothingEnabled = true
      sctx.drawImage(glow, 0, 0, small.width, small.height)
      ctx.imageSmoothingEnabled = true
      ctx.globalCompositeOperation = 'lighter'
      ctx.globalAlpha = lampsOn ? 0.35 : 0.8
      ctx.drawImage(small, 0, 0, vw, vh)
      ctx.globalAlpha = 1
      ctx.imageSmoothingEnabled = false
      ctx.globalCompositeOperation = 'source-over'

      // Rain, only outside.
      if (when.weather === 'rain' || when.weather === 'storm') {
        rctx.globalCompositeOperation = 'source-over'
        rctx.clearRect(0, 0, vw, vh)
        rctx.fillStyle = dark ? '#8aa0c8' : '#d8e4f4'
        for (const d of drops) {
          const y = (d.y * vh + t * 260 * d.s) % vh
          const x = (d.x * vw - t * 60 * d.s + vw * 40) % vw
          rctx.globalAlpha = 0.5
          rctx.fillRect(Math.round(x), Math.round(y), 1, 4)
          rctx.fillRect(Math.round(x) - 1, Math.round(y) + 4, 1, 2)
        }
        rctx.globalAlpha = 1
        rctx.globalCompositeOperation = 'destination-out'
        rctx.drawImage(world.indoor, ox, oy, vw, vh, 0, 0, vw, vh)
        ctx.drawImage(rain, 0, 0)
      }

      // The city fades a little towards the edges of the screen: the lab is what you look at.
      const vg = ctx.createRadialGradient(vw / 2, vh / 2, Math.min(vw, vh) * 0.35, vw / 2, vh / 2, Math.hypot(vw, vh) * 0.55)
      vg.addColorStop(0, 'rgba(8,12,22,0)')
      vg.addColorStop(1, 'rgba(8,12,22,0.42)')
      ctx.fillStyle = vg
      ctx.fillRect(0, 0, vw, vh)

      // Speech bubbles are not of the world: drawn last, never in the dark.
      inWorld(ctx)
      for (const n of names) ctx.drawImage(n.canvas, Math.round(n.at[0] - n.canvas.width / 2), Math.round(n.at[1]))
      for (const ag of agents) {
        if (!ag.bubble || ag.path.length || ag.away) continue
        const [x, y] = screen(ag.a, ag.b)
        drawBubble(ctx, x + 6, y - 50, ag.bubble, t, ag.seed)
      }
      plain(ctx)
    },
  }
}
