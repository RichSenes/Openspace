import type { AgentState } from '../art/agentStates'
import type { Facing } from '../art/walker'

/** Why an agent gets up: to read at a shelf, run something at a rack, plan at a board, or take a break. */
export type Errand = 'documents' | 'commands' | 'plans' | 'break'

/** A point of the world, in pixels: the top left corner of a standing agent's 24 by 32 frame. */
export interface Spot {
  x: number
  y: number
}

/** Where an agent stands to use a piece of furniture, and which way it then faces. */
export interface StationSpot extends Spot {
  errand: Errand
  facing: Facing
}

/** A desk and what its agent can reach from it: its seat, a way out on each side, and the stations of its room. */
export interface DeskPlace {
  deskId: string
  roomId: string
  seat: Spot
  /** Beside the desk, left then right, at the height of the seat. */
  exits: readonly [Spot, Spot]
  stations: readonly StationSpot[]
}

/** What the agent at a desk is up to now. */
export interface DeskDoing {
  state: AgentState
  /** The step it is on, such as "Reading header.php"; a new one may send it somewhere. */
  doing?: string | null
}

/** An agent away from its desk, as drawn now. */
export interface Walker {
  deskId: string
  roomId: string
  x: number
  y: number
  facing: Facing
  /** Frame of the legs: still at 0. */
  step: number
  /** Holding a cup, on a break. */
  sip: boolean
  /** In a chat: whether it speaks now, and what its made-up bubble shows. */
  chat?: { speaking: boolean; glyph: number }
}

const DOCUMENTS = /^(Reading|Searching|Listing|Looking (through|at)|Looking something up online|Fetching something from the web)\b/
const COMMANDS = /^(Running|Building|Checking|Installing|Starting|Publishing|Deploying|Updating the database)\b/
const PLANS = /^(Planning|Delegating|Thinking|Writing the plan)\b/

/** Where a step would send an agent, if anywhere: work by what it does, a break when it has nothing to do. */
export function errandOf(now: DeskDoing): Errand | null {
  if (now.state === 'working' || now.state === 'reading') {
    const doing = now.doing ?? ''
    if (DOCUMENTS.test(doing)) return 'documents'
    if (COMMANDS.test(doing)) return 'commands'
    if (PLANS.test(doing)) return 'plans'
    return null
  }
  return now.state === 'idle' || now.state === 'done' ? 'break' : null
}

/** Walking pace, in pixels a second; back to the desk faster when someone needs it. */
export const PACE = 24
export const HURRY = 48
/** The farthest a station may be, walking: a stroll, never a marathon. */
export const REACH = 200
/** Chance that a new step sends an agent to a station, when one is in reach. */
const WANDER = 0.35
/** Seconds to stay at least and at most; a break lasts a little longer. */
const MIN_STAY = 4000
const MAX_STAY = 18000
const BREAK_STAY = 10000
/** Time after coming back before the next walk. */
const REST = 20000
/** Breaks: every slot of this length, an agent with nothing to do may take one. */
const BREAK_SLOT = 150000
const BREAK_CHANCE = 0.3
/** Agents who meet stop for this long, and the same two not again before the next. */
const CHAT = 3500
const CHAT_AGAIN = 30000
const TURN = 1400
export const GLYPHS = 5

/** A number from 0 to 1, always the same for the same text. */
export function chance(text: string): number {
  let hash = 2166136261
  for (const ch of text) hash = Math.imul(hash ^ ch.charCodeAt(0), 16777619) >>> 0
  return (hash % 10007) / 10007
}

const lengthOf = (path: readonly Spot[]) => path.slice(1).reduce((sum, p, i) => sum + Math.abs(p.x - path[i].x) + Math.abs(p.y - path[i].y), 0)

/** The way from the seat to a station: out beside the desk, along, then across. */
export function routeTo(place: DeskPlace, station: Spot): Spot[] {
  const exit = nearestExit(place, station)
  return [place.seat, exit, { x: exit.x, y: station.y }, station]
}

const nearestExit = (place: DeskPlace, to: Spot) =>
  Math.abs(place.exits[0].x - to.x) <= Math.abs(place.exits[1].x - to.x) ? place.exits[0] : place.exits[1]

/** The way back to the seat from wherever the agent is. */
function routeBack(place: DeskPlace, from: Spot): Spot[] {
  const exit = nearestExit(place, from)
  return [from, { x: exit.x, y: from.y }, exit, place.seat]
}

/** Where a walk has got to, and which way it faces, after `distance` pixels. */
function along(path: readonly Spot[], distance: number): { at: Spot; facing: Facing; done: boolean } {
  let left = distance
  for (let i = 1; i < path.length; i++) {
    const [a, b] = [path[i - 1], path[i]]
    const length = Math.abs(b.x - a.x) + Math.abs(b.y - a.y)
    if (left < length) {
      const t = left / length
      const facing: Facing = b.y === a.y ? (b.x > a.x ? 'right' : 'left') : b.y < a.y ? 'back' : 'front'
      return { at: { x: Math.round(a.x + (b.x - a.x) * t), y: Math.round(a.y + (b.y - a.y) * t) }, facing, done: false }
    }
    left -= length
  }
  return { at: path[path.length - 1], facing: 'front', done: true }
}

interface Trip {
  path: Spot[]
  start: number
  pace: number
  /** Stops along the way for a chat: they do not count as walking. */
  pauses: { from: number; to: number }[]
}

type Outing =
  | { mode: 'going'; trip: Trip; station: StationSpot }
  | { mode: 'there'; station: StationSpot; since: number; stay: number }
  | { mode: 'back'; trip: Trip }

const walked = (trip: Trip, now: number) =>
  ((now - trip.start - trip.pauses.reduce((sum, p) => sum + Math.max(0, Math.min(now, p.to) - p.from), 0)) * trip.pace) / 1000
const pausedNow = (trip: Trip, now: number) => trip.pauses.some((p) => p.from <= now && now < p.to)

/**
 * Who is away from their desk and where: now and then a new step sends an agent to a
 * station of its room that fits it (documents, commands, plans), and an agent with
 * nothing to do takes a break at the coffee. It stays while the step lasts, within
 * limits, and walks back; it hurries back when it needs you. Agents who meet stop for a
 * chat. Everything follows the clock and the desks' names, so redraws agree.
 */
export class WalkStage {
  private readonly breakSlot: number

  /** `breakSlot` shortens the wait between breaks, for the sprite lab. */
  constructor(breakSlot = BREAK_SLOT) {
    this.breakSlot = breakSlot
  }

  private readonly outings = new Map<string, Outing>()
  private readonly lastDoing = new Map<string, string>()
  private readonly backAt = new Map<string, number>()
  private readonly breaks = new Map<string, number>()
  private readonly chats = new Map<string, { since: number; until: number }>()
  private places = new Map<string, DeskPlace>()

  update(places: readonly DeskPlace[], doing: ReadonlyMap<string, DeskDoing>, now: number): void {
    this.places = new Map(places.map((p) => [p.deskId, p]))
    for (const deskId of this.outings.keys()) if (!this.places.has(deskId)) this.outings.delete(deskId)
    for (const place of places) this.step(place, doing.get(place.deskId) ?? { state: 'idle' }, now)
    this.meet(now)
  }

  private step(place: DeskPlace, now: DeskDoing, time: number): void {
    const outing = this.outings.get(place.deskId)
    const errand = errandOf(now)
    const key = now.doing ?? ''
    const fresh = key !== '' && this.lastDoing.get(place.deskId) !== key
    if (key) this.lastDoing.set(place.deskId, key)
    const needed = now.state === 'waiting'

    if (!outing) {
      if (needed || errand === null || time - (this.backAt.get(place.deskId) ?? -Infinity) < REST) return
      const choices = place.stations.filter((s) => s.errand === errand)
      if (choices.length === 0) return
      const roll = errand === 'break' ? this.breakRoll(place.deskId, time) : fresh ? chance(`${place.deskId}:${key}`) : 1
      if (roll >= (errand === 'break' ? BREAK_CHANCE : WANDER)) return
      const station = choices[Math.floor(chance(`${place.deskId}:${key}:${time}`) * choices.length)]
      const path = routeTo(place, station)
      if (lengthOf(path) > REACH) return
      this.outings.set(place.deskId, { mode: 'going', trip: { path, start: time, pace: PACE, pauses: [] }, station })
      return
    }

    if (outing.mode === 'going') {
      const where = along(outing.trip.path, walked(outing.trip, time))
      if (needed || errand !== outing.station.errand) this.goBack(place, where.at, time, needed)
      else if (where.done) {
        const stay = outing.station.errand === 'break' ? BREAK_STAY + chance(place.deskId) * 4000 : MAX_STAY
        this.outings.set(place.deskId, { mode: 'there', station: outing.station, since: time, stay })
      }
      return
    }

    if (outing.mode === 'there') {
      const stayed = time - outing.since
      const over = errand !== outing.station.errand && stayed >= MIN_STAY
      if (needed || over || stayed >= outing.stay) this.goBack(place, outing.station, time, needed)
      return
    }

    if (needed && outing.trip.pace !== HURRY) this.goBack(place, along(outing.trip.path, walked(outing.trip, time)).at, time, true)
    else if (along(outing.trip.path, walked(outing.trip, time)).done) {
      this.outings.delete(place.deskId)
      this.backAt.set(place.deskId, time)
    }
  }

  /** A break comes at most once a slot, at a moment of the slot of its own. */
  private breakRoll(deskId: string, time: number): number {
    const slot = Math.floor(time / this.breakSlot)
    if (this.breaks.get(deskId) === slot) return 1
    const at = slot * this.breakSlot + chance(`${deskId}:at:${slot}`) * Math.max(0, this.breakSlot - BREAK_STAY * 2)
    if (time < at) return 1
    this.breaks.set(deskId, slot)
    return chance(`${deskId}:break:${slot}`)
  }

  private goBack(place: DeskPlace, from: Spot, time: number, hurry: boolean): void {
    this.outings.set(place.deskId, { mode: 'back', trip: { path: routeBack(place, from), start: time, pace: hurry ? HURRY : PACE, pauses: [] } })
  }

  /** Two agents of a room who come close stop for a chat; both at stations, they chat while they stay. */
  private meet(time: number): void {
    const away = [...this.outings.keys()].map((id) => this.at(id, time)).filter((w): w is Walker => w !== undefined)
    for (let i = 0; i < away.length; i++)
      for (let j = i + 1; j < away.length; j++) {
        const [a, b] = [away[i], away[j]]
        if (a.roomId !== b.roomId || Math.abs(a.x - b.x) > 30 || Math.abs(a.y - b.y) > 12) continue
        const pair = [a.deskId, b.deskId].sort().join('+')
        const last = this.chats.get(pair)
        if (last && time < last.until) continue
        if (last && time - last.until < CHAT_AGAIN && !this.bothThere(a.deskId, b.deskId)) continue
        const hurried = [a.deskId, b.deskId].some((id) => {
          const o = this.outings.get(id)
          return o?.mode === 'back' && o.trip.pace === HURRY
        })
        if (hurried) continue
        this.chats.set(pair, { since: last && time - last.until < 1000 ? last.since : time, until: time + CHAT })
        for (const id of [a.deskId, b.deskId]) {
          const o = this.outings.get(id)
          if (o && o.mode !== 'there' && !pausedNow(o.trip, time)) o.trip.pauses.push({ from: time, to: time + CHAT })
        }
      }
  }

  private bothThere(a: string, b: string): boolean {
    return this.outings.get(a)?.mode === 'there' && this.outings.get(b)?.mode === 'there'
  }

  /** The agent of a desk as drawn now, or undefined while it sits at its desk. */
  at(deskId: string, time: number): Walker | undefined {
    const outing = this.outings.get(deskId)
    const place = this.places.get(deskId)
    if (!outing || !place) return undefined
    const base = { deskId, roomId: place.roomId }
    let walker: Walker
    if (outing.mode === 'there') {
      const sip = outing.station.errand === 'break' && Math.floor((time - outing.since) / 2500) % 2 === 1
      walker = { ...base, x: outing.station.x, y: outing.station.y, facing: outing.station.facing, step: 0, sip }
    } else {
      const where = along(outing.trip.path, walked(outing.trip, time))
      const paused = pausedNow(outing.trip, time)
      walker = { ...base, ...where.at, facing: paused ? 'front' : where.facing, step: paused || where.done ? 0 : Math.floor(walked(outing.trip, time) / 4) % 4, sip: false }
    }
    const chat = [...this.chats.entries()].find(([pair, c]) => pair.split('+').includes(deskId) && c.since <= time && time < c.until)
    if (chat) {
      const [pair, c] = chat
      const first = pair.split('+')[0] === deskId
      const turn = Math.floor((time - c.since) / TURN)
      walker.facing = 'front'
      walker.sip = false
      walker.chat = { speaking: (turn % 2 === 0) === first, glyph: Math.floor(chance(`${pair}:${turn}`) * GLYPHS) }
    }
    return walker
  }
}
