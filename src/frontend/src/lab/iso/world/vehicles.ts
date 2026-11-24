import { box, iso, onLeftFace, onRightFace, shades, YF } from '../iso'
import type { Prop } from '../props'
import { mix, Painter } from '../px'

/**
 * The city's vehicles, of several makes: small cars, saloons, vans, buses. Each is drawn pointing
 * +a; seen from the `front` (driving towards you) its headlights show, from the `back` its tail
 * lights, `parked` neither. Mirrored, they drive along b.
 */

export type Make = 'small' | 'saloon' | 'van' | 'bus'

const SIZE: Record<Make, { da: number; db: number; body: number; cabin: number; glassFrom: number; glassTo: number }> = {
  small: { da: 22, db: 12, body: 6, cabin: 7, glassFrom: 4, glassTo: 16 },
  saloon: { da: 30, db: 13, body: 7, cabin: 6, glassFrom: 7, glassTo: 22 },
  van: { da: 32, db: 14, body: 16, cabin: 0, glassFrom: 24, glassTo: 31 },
  bus: { da: 52, db: 15, body: 24, cabin: 0, glassFrom: 2, glassTo: 50 },
}

export function vehicle(make: Make, colour: string, view: 'front' | 'back' | 'parked'): Prop {
  const { da, db, body, cabin, glassFrom, glassTo } = SIZE[make]
  const extra = 6
  const top = body + cabin + 12
  const w = 2 * (da + db) + 2 * extra
  const h = Math.ceil((da + db) * YF) + top + 2 * extra
  const ax = 2 * db + extra
  const ay = top + extra
  const p = new Painter(w, h)
  const at = (a: number, b: number, z = 0): [number, number] => {
    const [x, y] = iso(a, b, z)
    return [ax + x, ay + y]
  }
  p.poly([at(-1, -1), at(da + 2, -1), at(da + 2, db + 2), at(-1, db + 2)], '#0b1220', 0.28)
  for (const a of make === 'bus' ? [8, da - 10] : [5, da - 6]) {
    for (const b of [db, 0]) {
      const [x, y] = at(a, b, 3)
      p.ellipse(x, y, 3.5, 3.2, '#15181e')
      p.set(x, y, '#5d6878')
    }
  }
  box(p, at, 0, 0, 2, da, db, body, shades(colour))
  if (cabin) {
    box(p, at, glassFrom, 1.5, 2 + body, glassTo - glassFrom, db - 3, cabin, { top: mix(colour, '#ffffff', 0.2), left: '#2b3a4c', right: '#22303f' })
  } else {
    // Vans and buses: windows on the side, a windscreen at the front.
    const from = make === 'bus' ? 8 : 10
    for (let u = glassFrom * 2; u < glassTo * 2; u++) for (let v = from; v < body - 3; v++) if (make !== 'bus' || u % 9 !== 0) onLeftFace(p, at, 0, db, 2, u, v, '#2b3a4c')
    for (let u = 2; u < 2 * db - 2; u++) for (let v = from; v < body - 3; v++) onRightFace(p, at, da, 0, 2, u, v, '#22303f')
  }
  // A stripe along the side.
  const stripe = make === 'bus' ? '#ffd27a' : mix(colour, '#ffffff', 0.35)
  for (let u = 2; u < 2 * da - 2; u++) onLeftFace(p, at, 0, db, 2, u, 3, stripe)
  p.emitting = view !== 'parked'
  for (let u = 2; u < 2 * db - 2; u++) if (u < 5 || u > 2 * db - 6 || make === 'saloon') onRightFace(p, at, da, 0, 2, u, 5, view === 'back' ? '#ff4a5a' : view === 'front' ? '#fff4c8' : '#c9d1da')
  p.emitting = false
  p.outline('#18202b')
  // Two beams from the headlights along the road ahead: +a for a car coming towards you, -a for
  // one driving away (its tail lights are what you see).
  const ahead = view === 'back' ? -1 : 1
  const [ux, uy] = iso(ahead, 0)
  const n = Math.hypot(ux, uy)
  const lights =
    view === 'parked'
      ? undefined
      : [db * 0.25, db * 0.75].map((b) => {
          const [dx, dy] = iso(view === 'back' ? 0 : da, b, 4)
          return { dx, dy, r: 46, color: '#fff2c8', beam: [ux / n, uy / n] as [number, number] }
        })
  return { da, db, sprite: { canvas: p.canvas(), ax, ay, glow: p.glowCanvas() ?? undefined }, lights }
}
