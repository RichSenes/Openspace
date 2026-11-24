import { mix, Painter } from './px'

/**
 * A scientist in a lab coat, about 46 pixels tall, painted pixel by pixel. Two drawings, the
 * front three-quarter (walking down-right) and the back (walking up-right); flipped, they give
 * the other two directions, as in Habbo. Hair, skin and the shirt under the coat (the colour of
 * their department) make each one somebody.
 */

export type HairStyle = 'short' | 'bun' | 'long' | 'pony' | 'curly'
export type Pose = 'stand' | 'walk' | 'notes' | 'think' | 'coffee' | 'type'
/** Screen directions: SE and SW face the viewer, NE and NW turn their back. */
export type Facing = 'SE' | 'SW' | 'NE' | 'NW'

export interface Look {
  skin: string
  hair: string
  style: HairStyle
  shirt: string
  glasses?: boolean
  trousers?: string
  /** A coat other than the lab's white one: people in the street. */
  coat?: string
}

export const W = 32
export const H = 58
/** The pixel between the feet: it stands on the floor point. */
export const FOOT_X = 16
export const FOOT_Y = 55

const OUTLINE = '#1a2230'
const EYE = '#251d2c'

/** How many frames each pose has. */
export const FRAMES: Record<Pose, number> = {
  stand: 1,
  walk: 4,
  notes: 2,
  think: 1,
  coffee: 2,
  type: 2,
}

export function paintScientist(look: Look, back: boolean, pose: Pose, frame: number): Painter {
  const p = new Painter(W, H)
  const COAT = look.coat ?? '#e6ecf2'
  const COAT_SHADE = look.coat ? mix(look.coat, '#0b1020', 0.22) : '#b8c4d0'
  const COAT_DEEP = look.coat ? mix(look.coat, '#0b1020', 0.4) : '#93a1b2'
  const COAT_LIGHT = look.coat ? mix(look.coat, '#ffffff', 0.25) : '#f8fbfd'
  const skinShade = mix(look.skin, '#5a2a30', 0.22)
  const hairShade = mix(look.hair, '#0a0a14', 0.35)
  const hairLight = mix(look.hair, '#ffffff', 0.25)
  const trousers = look.trousers ?? '#2e3644'
  const walk = pose === 'walk' ? [0, 1, 0, -1][frame % 4] : 0
  // The body rises a pixel between steps.
  const lift = pose === 'walk' && frame % 2 === 1 ? -1 : 0
  const y0 = lift

  // Hair that falls behind the body goes first.
  if (!back) {
    if (look.style === 'long') {
      p.rect(9, 10 + y0, 14, 18, look.hair)
      p.rect(21, 12 + y0, 2, 16, hairShade)
    }
    if (look.style === 'pony') {
      p.rect(7, 11 + y0, 3, 4, look.hair)
      p.rect(6, 14 + y0, 3, 9, look.hair)
      p.rect(8, 14 + y0, 1, 9, hairShade)
    }
    if (look.style === 'bun') p.ellipse(15, 5.5 + y0, 4, 3.5, look.hair)
  }

  // Legs and shoes: a step moves one leg forward and lifts the other.
  const leg = (x: number, shift: number, up: number) => {
    p.rect(x + shift, 43 + y0, 4, 10 - up - y0, trousers)
    p.rect(x + shift + 3, 43 + y0, 1, 10 - up - y0, mix(trousers, '#000000', 0.3))
    p.rect(x + shift - (back ? 0 : 0), 53 - up, back ? 4 : 5, 2, '#1d2129')
    if (!back) p.set(x + shift + 4, 53 - up, '#3a414e')
  }
  leg(11, walk === 1 ? 1 : walk === -1 ? -1 : 0, walk === -1 ? 1 : 0)
  leg(17, walk === 1 ? -1 : walk === -1 ? 1 : 0, walk === 1 ? 1 : 0)

  // The coat: wider at the hem, shaded on the right, away from the light.
  for (let y = 23; y <= 44; y++) {
    const hw = y === 23 ? 6 : y < 34 ? 7 : 8
    for (let x = 16 - hw; x < 16 + hw; x++) {
      const c = x >= 16 + hw - 3 ? COAT_SHADE : x === 16 - hw ? COAT_LIGHT : COAT
      p.set(x, y + y0, y === 44 ? COAT_SHADE : c)
    }
  }
  if (!back) {
    // Open at the front: the shirt in their department's colour, lapels, a pen in the pocket.
    for (let y = 23; y <= 29; y++) {
      const k = Math.floor((29 - y) / 2)
      for (let x = 18 - k; x <= 18 + k; x++) p.set(x, y + y0, y === 23 ? mix(look.shirt, '#000000', 0.3) : look.shirt)
      p.set(17 - k, y + y0, COAT_DEEP)
      p.set(19 + k, y + y0, COAT_SHADE)
    }
    for (let y = 30; y <= 44; y++) p.set(18, y + y0, COAT_DEEP)
    p.rect(11, 35 + y0, 3, 1, COAT_SHADE)
    p.rect(20, 27 + y0, 1, 2, look.shirt)
    p.set(20, 26 + y0, '#d6dde6')
    p.rect(12, 27 + y0, 2, 1, mix(look.shirt, '#ffffff', 0.3))
  } else {
    p.rect(11, 23 + y0, 10, 1, COAT_SHADE)
    p.rect(12, 33 + y0, 8, 1, COAT_SHADE)
    for (let y = 37; y <= 44; y++) p.set(16, y + y0, COAT_DEEP)
  }

  // Arms: they swing against the legs when walking.
  const sleeve = (x: number, top: number, bottom: number, shade: boolean) => {
    p.rect(x, top + y0, 3, bottom - top, shade ? COAT_SHADE : COAT)
    p.rect(x + (shade ? 2 : 0), top + y0, 1, bottom - top, shade ? COAT_DEEP : COAT_LIGHT)
  }
  const hand = (x: number, y: number) => {
    p.rect(x, y + y0, 3, 2, look.skin)
    p.set(x + 2, y + 1 + y0, skinShade)
  }
  const reach = !back ? pose : pose === 'walk' || pose === 'stand' ? pose : 'reach'
  if (reach === 'walk' || reach === 'stand') {
    const s = reach === 'walk' ? walk : 0
    sleeve(8, 24, 35 - s, false)
    hand(8 - (back ? 0 : 0), 35 - s)
    sleeve(21, 24, 35 + s, true)
    hand(21, 35 + s)
  } else if (reach === 'reach') {
    // Seen from behind, hands busy in front: the sleeves go forward and out of sight.
    sleeve(8, 24, 31, false)
    sleeve(21, 24, 31, true)
  } else if (reach === 'notes') {
    sleeve(8, 24, 31, false)
    p.rect(10, 30 + y0, 3, 2, COAT)
    // The clipboard, held against the chest; the pen hand writes.
    p.rect(11, 28 + y0, 8, 9, '#7b5a3c')
    p.rect(12, 29 + y0, 6, 7, '#f3f0e6')
    for (let k = 0; k < 3; k++) p.rect(13, 30 + k * 2 + y0, 2 + ((k + frame) % 3), 1, '#8d96a6')
    p.rect(14, 27 + y0, 2, 1, '#c9cdd3')
    hand(10, 32)
    sleeve(21, 24, 31, true)
    p.rect(19, 30 + y0, 3, 2, COAT_SHADE)
    hand(17 + (frame % 2), 31 + (frame % 2))
    p.set(17 + (frame % 2), 30 + (frame % 2) + y0, '#3a5a8c')
  } else if (reach === 'think') {
    sleeve(8, 24, 35, false)
    hand(8, 35)
    // A hand at the chin.
    sleeve(21, 24, 29, true)
    p.rect(19, 25 + y0, 3, 5, COAT_SHADE)
    p.rect(18, 22 + y0, 2, 4, COAT)
    hand(17, 20)
  } else if (reach === 'coffee') {
    sleeve(8, 24, 35, false)
    hand(8, 35)
    const up = frame % 2 === 1 ? 4 : 0
    sleeve(21, 24, 30 - up, true)
    p.rect(19, 29 - up + y0, 3, 2, COAT_SHADE)
    p.rect(18, 26 - up + y0, 4, 5, '#f2f2f0')
    p.rect(18, 28 - up + y0, 4, 1, look.shirt)
    p.rect(21, 27 - up + y0, 1, 3, '#cfd3d8')
    hand(17, 30 - up)
  } else if (reach === 'type') {
    sleeve(8, 24, 31, false)
    sleeve(21, 24, 31, true)
    p.rect(11, 31 + y0, 3, 2, COAT)
    p.rect(18, 31 + y0, 3, 2, COAT_SHADE)
    hand(12, 33 - (frame % 2))
    hand(18, 33 - ((frame + 1) % 2))
  }

  // Neck and head.
  p.rect(14, 21 + y0, 4, 2, skinShade)
  p.rect(14, 21 + y0, 3, 1, look.skin)
  const rows: [number, number][] = [
    [12, 19],
    [11, 20],
    [10, 21],
    [10, 21],
    [10, 21],
    [10, 21],
    [10, 21],
    [10, 21],
    [10, 21],
    [10, 21],
    [11, 20],
    [12, 19],
    [13, 18],
  ]
  rows.forEach(([x0, x1], r) => {
    for (let x = x0; x <= x1; x++) p.set(x, 8 + r + y0, x >= x1 - 1 ? skinShade : look.skin)
  })

  if (!back) {
    // A face turned a little to the right.
    // Turned three-quarters: features to the right, the left ear showing.
    p.rect(15, 14 + y0, 1, 2, EYE)
    p.rect(19, 14 + y0, 1, 2, EYE)
    p.set(14, 14 + y0, mix(look.skin, EYE, 0.35))
    p.set(20, 14 + y0, mix(look.skin, EYE, 0.35))
    p.set(18, 16 + y0, skinShade)
    p.set(19, 16 + y0, skinShade)
    p.rect(17, 18 + y0, 2, 1, '#b56a66')
    p.set(14, 17 + y0, '#f0a59c', 0.55)
    p.set(20, 17 + y0, '#f0a59c', 0.4)
    p.rect(11, 13 + y0, 2, 4, skinShade)
    p.set(12, 14 + y0, mix(skinShade, '#5a2a30', 0.3))
    if (look.glasses) {
      p.rect(14, 13 + y0, 3, 1, '#2b3442')
      p.rect(18, 13 + y0, 3, 1, '#2b3442')
      p.set(17, 14 + y0, '#2b3442')
      p.rect(11, 14 + y0, 3, 1, '#2b3442')
      p.rect(14, 14 + y0, 3, 2, '#cfe6ff', 0.35)
      p.rect(18, 14 + y0, 3, 2, '#cfe6ff', 0.35)
      p.rect(14, 16 + y0, 3, 1, '#2b3442', 0.6)
      p.rect(18, 16 + y0, 3, 1, '#2b3442', 0.6)
    }
    // Hair: a cap with a fringe, then the style's own pieces.
    const cap: [number, number, number][] = [
      [6, 12, 19],
      [7, 11, 20],
      [8, 10, 21],
      [9, 10, 21],
      [10, 10, 21],
      [11, 10, 12],
      [12, 10, 11],
      [13, 10, 10],
    ]
    if (look.style === 'curly') cap.unshift([5, 13, 18])
    for (const [y, x0, x1] of cap) for (let x = x0; x <= x1; x++) p.set(x, y + y0, x >= x1 - 1 ? hairShade : look.hair)
    // The fringe, swept to one side.
    p.rect(15, 11 + y0, 6, 1, look.hair)
    p.rect(19, 12 + y0, 3, 1, look.hair)
    p.rect(21, 13 + y0, 1, 2, hairShade)
    p.rect(13, 7 + y0, 3, 1, hairLight)
    p.set(12, 8 + y0, hairLight)
    if (look.style === 'curly') {
      for (const [x, y] of [
        [9, 9],
        [22, 9],
        [9, 12],
        [22, 12],
        [9, 15],
        [22, 15],
        [12, 4],
        [19, 4],
      ]) {
        p.set(x, y + y0, look.hair)
        p.set(x, y + 1 + y0, hairShade)
      }
      p.rect(10, 13 + y0, 1, 4, look.hair)
      p.rect(21, 15 + y0, 1, 3, hairShade)
      p.set(14, 6 + y0, hairShade)
      p.set(17, 7 + y0, hairShade)
    }
    if (look.style === 'long') {
      p.rect(10, 13 + y0, 1, 10, look.hair)
      p.rect(21, 13 + y0, 2, 10, hairShade)
    }
    if (look.style === 'bun') {
      p.set(14, 4 + y0, hairLight)
      p.rect(18, 3 + y0, 1, 4, '#d9b45a')
    }
  } else {
    // From behind: all hair, then the nape and the ears.
    rows.forEach(([x0, x1], r) => {
      if (r > 9) return
      for (let x = x0; x <= x1; x++) p.set(x, 8 + r + y0, x >= x1 - 1 ? hairShade : look.hair)
    })
    for (let x = 12; x <= 19; x++) p.set(x, 6 + y0, look.hair)
    for (let x = 11; x <= 20; x++) p.set(x, 7 + y0, look.hair)
    p.rect(13, 7 + y0, 4, 1, hairLight)
    p.rect(10, 14 + y0, 1, 2, skinShade)
    p.rect(21, 14 + y0, 1, 2, skinShade)
    if (look.style === 'long') {
      p.rect(10, 17 + y0, 12, 12, look.hair)
      p.rect(20, 17 + y0, 2, 12, hairShade)
      p.rect(15, 18 + y0, 1, 9, hairShade)
    }
    if (look.style === 'pony') {
      p.rect(14, 15 + y0, 4, 3, hairShade)
      p.rect(15, 18 + y0, 3, 9, look.hair)
      p.rect(17, 18 + y0, 1, 9, hairShade)
    }
    if (look.style === 'bun') {
      p.ellipse(16, 8 + y0, 4, 3.5, look.hair)
      p.rect(14, 7 + y0, 2, 1, hairLight)
      p.rect(12, 7 + y0, 9, 1, '#d9b45a')
    }
    if (look.style === 'curly') {
      for (const [x, y] of [
        [9, 9],
        [22, 9],
        [9, 12],
        [22, 12],
        [12, 5],
        [19, 5],
        [16, 4],
      ])
        p.set(x, y + y0, look.hair)
      p.rect(11, 17 + y0, 10, 2, look.hair)
    }
  }

  p.outline(OUTLINE)
  return p
}

export interface Frames {
  get(facing: Facing, pose: Pose, frame: number): HTMLCanvasElement
}

/** All of a scientist's drawings, painted on first use. */
export function scientistFrames(look: Look): Frames {
  const cache = new Map<string, HTMLCanvasElement>()
  return {
    get(facing, pose, frame) {
      const f = frame % FRAMES[pose]
      const key = `${facing}${pose}${f}`
      let c = cache.get(key)
      if (!c) {
        const back = facing === 'NE' || facing === 'NW'
        const p = paintScientist(look, back, pose, f)
        c = (facing === 'SW' || facing === 'NW' ? p.flipped() : p).canvas()
        cache.set(key, c)
      }
      return c
    },
  }
}

/** Which way someone looks while walking along a floor axis. */
export function facingOf(da: number, db: number): Facing {
  if (Math.abs(da) >= Math.abs(db)) return da >= 0 ? 'SE' : 'NW'
  return db >= 0 ? 'SW' : 'NE'
}
