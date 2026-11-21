import { useEffect, useMemo, useRef, useState } from 'react'
import { mixHex, type FaceState } from '../faceMachine'
import { bendPoint, makeWeb, quadAt } from './faceGeometry'
import { useFaceLoop, type FaceProps } from './useFaceLoop'

const INK = '#ece8df'
const SCALE = 100
const SIGNALS = 12

const SIGNALS_BY_STATE: Record<FaceState, number> = {
  idle: 2,
  listening: 4,
  thinking: 12,
  speaking: 8,
  pleased: 7,
  unsure: 3,
}

const SHAPE_BY_STATE: Record<FaceState, { k: number; lean: number; tremble: number }> = {
  idle: { k: 1, lean: 0, tremble: 0 },
  listening: { k: 0.97, lean: 9, tremble: 0 },
  thinking: { k: 0.8, lean: -3, tremble: 0 },
  speaking: { k: 1, lean: 4, tremble: 0 },
  pleased: { k: 1.07, lean: 2, tremble: 0 },
  unsure: { k: 0.95, lean: -6, tremble: 1.2 },
}

interface Signal {
  edge: number
  t: number
  speed: number
  forward: boolean
}

/** Direction C: a neural web of ink strokes that tightens when it thinks and lights up along paths. */
export function InkWeb({ state, modeHex, reduced }: FaceProps) {
  const web = useMemo(() => makeWeb(30, 11), [])
  const edgeRefs = useRef<(SVGPathElement | null)[]>([])
  const sketchRefs = useRef<(SVGPathElement | null)[]>([])
  const nodeRefs = useRef<(SVGCircleElement | null)[]>([])
  const sigRefs = useRef<(SVGCircleElement | null)[]>([])
  const hubRef = useRef<SVGCircleElement>(null)
  const shape = useRef({ k: 1, lean: 0, tremble: 0 })
  const signals = useRef<Signal[]>([])
  const lit = useRef<number[]>(web.nodes.map(() => 0))
  const [drawn, setDrawn] = useState(false)

  useEffect(() => {
    const id = setTimeout(() => setDrawn(true), reduced ? 0 : 1900)
    return () => clearTimeout(id)
  }, [reduced])

  useFaceLoop(state, (f) => {
    const { look, time, dt } = f
    const target = SHAPE_BY_STATE[state]
    const s = shape.current
    const ease = Math.min(1, dt * 3)
    s.k += (target.k - s.k) * ease
    s.lean += (target.lean - s.lean) * ease
    s.tremble += (target.tremble - s.tremble) * ease
    const level = state === 'speaking' && !reduced ? 0.5 + 0.5 * Math.sin(time * 8) * Math.sin(time * 2.9 + 1) : 0
    const k = s.k + level * 0.05
    const motion = reduced ? 0 : 1

    const pos = web.nodes.map((n, i) => {
      const breathe = Math.sin(time * 0.9 * look.speed + n.phase * 6.28) * 2.2 * motion
      const shake = Math.sin(time * 13 + i * 1.7) * s.tremble * motion
      // Nodes on the chat's side lean further when it listens.
      const lean = s.lean * (0.5 + 0.5 * n.x)
      return [n.x * SCALE * k + lean + breathe * 0.6 + shake, n.y * SCALE * k + breathe] as [number, number]
    })

    const stroke = mixHex(INK, look.color, 0.3)
    web.edges.forEach((e, i) => {
      const [ax, ay] = pos[e.a]
      const [bx, by] = pos[e.b]
      const [cx, cy] = bendPoint(ax, ay, bx, by, e.bend)
      const [sx, sy] = bendPoint(ax, ay, bx, by, e.bend * 1.4 + 0.04)
      edgeRefs.current[i]?.setAttribute('d', `M${ax.toFixed(1)} ${ay.toFixed(1)}Q${cx.toFixed(1)} ${cy.toFixed(1)} ${bx.toFixed(1)} ${by.toFixed(1)}`)
      edgeRefs.current[i]?.setAttribute('stroke', stroke)
      sketchRefs.current[i]?.setAttribute('d', `M${(ax + 0.8).toFixed(1)} ${(ay - 0.6).toFixed(1)}Q${sx.toFixed(1)} ${sy.toFixed(1)} ${(bx - 0.6).toFixed(1)} ${(by + 0.8).toFixed(1)}`)
    })

    // Signals run along the strokes; when one arrives, the node it reaches lights up.
    const want = reduced ? 0 : SIGNALS_BY_STATE[state]
    const sigs = signals.current
    while (sigs.length < want) sigs.push(spawn(state, web.edges.length, pos, web.edges))
    if (sigs.length > want) sigs.length = want
    sigs.forEach((sg, j) => {
      sg.t += dt * sg.speed * look.speed
      if (sg.t >= 1) {
        const e = web.edges[sg.edge]
        lit.current[sg.forward ? e.b : e.a] = 1
        sigs[j] = spawn(state, web.edges.length, pos, web.edges)
      }
    })
    for (let j = 0; j < SIGNALS; j++) {
      const el = sigRefs.current[j]
      const sg = sigs[j]
      if (!el) continue
      if (!sg) {
        el.setAttribute('opacity', '0')
        continue
      }
      const e = web.edges[sg.edge]
      const [ax, ay] = pos[sg.forward ? e.a : e.b]
      const [bx, by] = pos[sg.forward ? e.b : e.a]
      const [cx, cy] = bendPoint(pos[e.a][0], pos[e.a][1], pos[e.b][0], pos[e.b][1], e.bend)
      const [x, y] = quadAt(ax, ay, cx, cy, bx, by, Math.min(1, sg.t))
      el.setAttribute('cx', x.toFixed(1))
      el.setAttribute('cy', y.toFixed(1))
      el.setAttribute('fill', look.color)
      el.setAttribute('opacity', String(Math.sin(Math.PI * Math.min(1, sg.t)) * 0.95))
    }

    web.nodes.forEach((n, i) => {
      const el = nodeRefs.current[i]
      if (!el) return
      lit.current[i] = Math.max(0, lit.current[i] - dt * 1.4)
      const glow = reduced ? (i % 4 === 0 ? look.energy * 0.6 : 0) : lit.current[i]
      el.setAttribute('cx', pos[i][0].toFixed(1))
      el.setAttribute('cy', pos[i][1].toFixed(1))
      el.setAttribute('r', (n.r * (1 + glow * 0.6) * (i === 0 ? 1 + look.energy * 0.4 + level * 0.3 : 1)).toFixed(2))
      el.setAttribute('fill', glow > 0.05 ? mixHex(INK, look.color, Math.min(1, glow + 0.2)) : INK)
    })
    const hub = hubRef.current
    if (hub) {
      hub.setAttribute('cx', pos[0][0].toFixed(1))
      hub.setAttribute('cy', pos[0][1].toFixed(1))
      hub.setAttribute('r', (10 + look.energy * 6 + level * 6).toFixed(1))
      hub.setAttribute('stroke', modeHex)
    }
  })

  return (
    <svg className={`face-ink ${drawn ? 'is-drawn' : 'is-drawing'}`} viewBox="-112 -112 224 224" aria-hidden="true">
      <circle ref={hubRef} className="ink-hub" r="12" fill="none" strokeWidth="1.2" strokeDasharray="2 3" />
      <g className="ink-sketch">
        {web.edges.map((_, i) => (
          <path key={i} ref={(el) => void (sketchRefs.current[i] = el)} pathLength={1} />
        ))}
      </g>
      <g className="ink-edges">
        {web.edges.map((e, i) => (
          <path
            key={i}
            ref={(el) => void (edgeRefs.current[i] = el)}
            pathLength={1}
            strokeWidth={1.1 + ((Math.abs(e.bend) * 7) % 1)}
            className={state === 'unsure' && drawn && i % 4 === 1 ? 'is-broken' : undefined}
            style={{ animationDelay: `${(i % 9) * 0.09}s` }}
          />
        ))}
      </g>
      <g>
        {Array.from({ length: SIGNALS }, (_, j) => (
          <circle key={j} ref={(el) => void (sigRefs.current[j] = el)} r="2.6" opacity="0" />
        ))}
      </g>
      <g className="ink-nodes">
        {web.nodes.map((n, i) => (
          <circle key={i} ref={(el) => void (nodeRefs.current[i] = el)} r={n.r} fill={INK} style={{ animationDelay: `${0.6 + (i % 7) * 0.1}s` }} />
        ))}
      </g>
    </svg>
  )
}

function spawn(state: FaceState, edges: number, pos: [number, number][], list: { a: number; b: number }[]): Signal {
  let edge = Math.floor(Math.random() * edges)
  if (state === 'listening') {
    // Listening: signals start on the side facing the chat.
    for (let tries = 0; tries < 6 && pos[list[edge].a][0] < 0 && pos[list[edge].b][0] < 0; tries++) edge = Math.floor(Math.random() * edges)
  }
  const e = list[edge]
  const da = Math.hypot(...pos[e.a])
  const db = Math.hypot(...pos[e.b])
  // Speaking sends signals outward from the hub; thinking draws them inward.
  const forward = state === 'speaking' ? db > da : state === 'thinking' ? db < da : Math.random() < 0.5
  const speed = state === 'thinking' ? 1.4 + Math.random() : state === 'unsure' ? 0.35 + Math.random() * 0.3 : 0.6 + Math.random() * 0.6
  return { edge, t: 0, speed, forward }
}
