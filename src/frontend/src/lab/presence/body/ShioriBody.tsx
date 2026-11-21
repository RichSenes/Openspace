import { useEffect, useRef } from 'react'
import type { FaceState } from '../../shiori/faceMachine'
import type { Mode } from '../../shiori/modes'
import { approach, blinkOpen, LOOKS, makeLife, POSES, stepLife, voiceLevel, type Pose } from './character'
import { BodyRenderer } from './renderer'
import { bodyRotation, mix3, MODE_HEAT, project, RIGS } from './rig'
import { PORTRAIT_UNITS, rng, type SkinId } from './shapes'
import { changeAt, type Change } from './transition'
import { expressionOf, makeFace, stepFace } from '../portrait/face'
import { drawPortrait, PORTRAIT_H, PORTRAIT_W } from '../portrait/portrait'
import { makeWorms, stepWorms, wormPositions } from './worms'

export interface BodyProps {
  state: FaceState
  skin: SkinId
  mode: Mode
  /** 0 smooth; otherwise screen pixels per art pixel. */
  pixel: number
  /** The world's palette, which Shiori snaps to in pixel mode. */
  palette: [number, number, number][]
  calm: boolean
  /** Where Shiori stands in the stage: centre (0..1 of width and height) and radius in CSS px. */
  focus: { x: number; y: number; size: number }
  /** Notes the HUD pins to her body: what she reads or cites. */
  notes: string[]
}

const WORMS = 9
const WORM_LENGTH = 26

/** Shiori herself: one canvas, one loop, and the HUD tags that follow her parts. */
export function ShioriBody(props: BodyProps) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const hud = useRef<HTMLDivElement>(null)
  const lines = useRef<SVGSVGElement>(null)
  const live = useRef(props)
  useEffect(() => {
    live.current = props
  })
  const failed = useRef(false)

  useEffect(() => {
    const c = canvas.current!
    let r: BodyRenderer
    try {
      r = new BodyRenderer(c)
    } catch (e) {
      failed.current = true
      c.dataset.failed = String(e instanceof Error ? e.message : e)
      return
    }
    const rand = rng(99)
    let pose: Pose = { ...POSES.idle }
    // The portrait: its own canvas at the art's size, and the face that moves it.
    const art = document.createElement('canvas')
    art.width = PORTRAIT_W
    art.height = PORTRAIT_H
    const artG = art.getContext('2d', { willReadFrequently: true })!
    let face = makeFace(live.current.state)
    let lastArt = -1
    let life = makeLife()
    let worms = makeWorms(WORMS, WORM_LENGTH)
    const wormBuf = new Float32Array(WORMS * WORM_LENGTH * 3)
    let phase = 0
    let voice = 0
    let shownSkin = live.current.skin
    let shownMode = live.current.mode
    let change: Change | null = null
    let last = performance.now()
    const start = last
    // HUD tags: where each one is drawn now, eased toward its part.
    const tags = new Map<number, { x: number; y: number }>()
    let raf = 0

    const frame = (nowMs: number) => {
      raf = requestAnimationFrame(frame)
      const p = live.current
      const dt = Math.min(0.05, (nowMs - last) / 1000)
      last = nowMs
      const time = (nowMs - start) / 1000
      const calm = p.calm

      // Changes of body or mode dissolve and come back.
      if (p.skin !== shownSkin && (!change || change.kind !== 'skin')) change = { kind: 'skin', start: time }
      else if (p.mode !== shownMode && !change) change = { kind: 'mode', start: time }
      const ch = changeAt(change, time, calm)
      if (ch.swapped) {
        if (change?.kind === 'skin') shownSkin = p.skin
        shownMode = p.mode
      }
      if (ch.done) change = null

      pose = approach(pose, POSES[p.state], dt, p.state === 'thinking' ? 3.5 : 5)
      life = stepLife(life, dt, rand, LOOKS[p.state], p.state === 'idle' ? 0.45 : 0.15)
      const motion = calm ? 0.25 : 1
      phase += dt * pose.speed * motion
      const targetVoice = p.state === 'speaking' && !calm ? voiceLevel(time) : 0
      voice += (targetVoice - voice) * (1 - Math.exp(-20 * dt))
      if (shownSkin === 'swarm') {
        worms = stepWorms(worms, dt, (0.25 + pose.heat * 1.1) * motion)
        wormPositions(worms, wormBuf)
      }

      const rect = c.getBoundingClientRect()
      const dpr = Math.min(2, window.devicePixelRatio || 1)
      const W = Math.max(1, Math.round(rect.width * dpr))
      const H = Math.max(1, Math.round(rect.height * dpr))
      if (c.width !== W || c.height !== H) {
        c.width = W
        c.height = H
      }
      const rig = RIGS[shownSkin]
      let sprite: HTMLCanvasElement | undefined
      if (shownSkin === 'portrait') {
        face = stepFace(face, dt, p.state, rand, calm)
        // Pixel art moves at its own pace: the drawing changes at most 24 times a second.
        if (time - lastArt > 1 / 24) {
          drawPortrait(artG, expressionOf(face), time * motion)
          lastArt = time
        }
        sprite = art
      }
      let scale = (p.focus.size * 2) / rect.height / 0.8
      // In pixel mode the portrait is drawn at a whole number of pixels per art pixel, or, when
      // smaller, one pixel per 2×2 or 3×3 art pixels averaged: in between, its points fall between
      // pixels and the face breaks up into lines and noise.
      let stride = 1
      if (shownSkin === 'portrait' && p.pixel) {
        const rows = H / p.pixel
        const perArt = (PORTRAIT_UNITS * rig.bodyScale * scale * rows) / 2 / PORTRAIT_H
        if (perArt >= 0.75) scale *= Math.max(1, Math.round(perArt)) / perArt
        else {
          stride = Math.min(4, Math.max(2, Math.round(1 / perArt)))
          scale *= 1 / stride / perArt
        }
      }
      const offset: [number, number] = [p.focus.x * 2 - 1, 1 - p.focus.y * 2]
      const { rot, eyeRot } = bodyRotation(shownSkin, phase, time * motion, life.gaze, pose.lean)
      const modeHeat = MODE_HEAT[shownMode]
      const hot = mix3(rig.hot, modeHeat, 0.45)

      r.render(
        {
          skin: shownSkin,
          phase,
          time,
          energy: pose.energy,
          voice,
          eyes: pose.eyes,
          open: pose.open * blinkOpen(life),
          pleased: pose.pleased,
          unsure: pose.unsure,
          lean: pose.lean,
          heat: pose.heat,
          gaze: life.gaze,
          rot,
          eyeRot,
          eyeCenter: rig.eyeCenter,
          eyeSize: rig.eyeSize,
          lift: rig.lift,
          bodyScale: rig.bodyScale,
          scale,
          offset,
          cool: rig.cool,
          hot,
          eye: rig.eye,
          ascii: ch.ascii,
          glitch: ch.glitch,
          worms: wormBuf,
          wormLength: WORM_LENGTH,
          sprite,
          stride,
        },
        { pixel: p.pixel, palette: p.pixel ? p.palette : [] },
        WORMS,
      )

      // HUD: each tag hangs off a part of the body, eased like it is on a spring.
      const box = hud.current
      const svg = lines.current
      if (!box || !svg) return
      const view = { w: rect.width, h: rect.height, scale, offset }
      const els = box.children
      const paths = svg.children
      for (let i = 0; i < els.length; i++) {
        const el = els[i] as HTMLElement
        const part = rig.parts[i % rig.parts.length]
        const at = project(part.at, rot, rig, view)
        // Tags alternate sides and keep their own row, so they never pile up.
        const side = i % 2 === 0 ? 1 : -1
        const cx = p.focus.x * rect.width
        const cy = p.focus.y * rect.height
        const row = cy - p.focus.size * 0.7 + i * p.focus.size * 0.62
        const want = { x: cx + side * (p.focus.size * 1.08 + 18), y: row }
        const cur = tags.get(i) ?? want
        const k = 1 - Math.exp(-(calm ? 30 : 7) * dt)
        cur.x += (want.x - cur.x) * k
        cur.y += (want.y - cur.y) * k
        // Never past the edges: a tag on the left grows leftward from its point.
        const tw = el.offsetWidth
        cur.x = side < 0 ? Math.max(tw + 8, cur.x) : Math.min(rect.width - tw - 8, cur.x)
        cur.y = Math.min(rect.height - 40, Math.max(64, cur.y))
        tags.set(i, cur)
        el.style.transform = `translate(${Math.round(cur.x)}px, ${Math.round(cur.y)}px) translateX(${side < 0 ? '-100%' : '0'})`
        el.dataset.side = side < 0 ? 'left' : 'right'
        const coord = el.querySelector('[data-coord]')
        if (coord) coord.textContent = `X${(at.x / rect.width).toFixed(3)} Y${(at.y / rect.height).toFixed(3)} Z${at.depth.toFixed(2)}`
        const path = paths[i] as SVGPathElement | undefined
        if (path) {
          const ex = cur.x
          const ey = cur.y + 9
          path.setAttribute('d', `M${at.x.toFixed(1)} ${at.y.toFixed(1)} L${(at.x + (ex - at.x) * 0.35).toFixed(1)} ${ey.toFixed(1)} L${ex.toFixed(1)} ${ey.toFixed(1)}`)
          const mark = paths[els.length + i] as SVGRectElement | undefined
          mark?.setAttribute('x', (at.x - 4).toFixed(1))
          mark?.setAttribute('y', (at.y - 4).toFixed(1))
        }
      }
    }
    raf = requestAnimationFrame(frame)
    return () => {
      cancelAnimationFrame(raf)
      r.dispose()
    }
  }, [])

  const rig = RIGS[props.skin]
  const tags = rig.parts.map((part, i) => ({ part, note: props.notes[i] }))

  return (
    <div className="pr-body">
      <canvas ref={canvas} className="pr-canvas" aria-hidden="true" />
      <svg ref={lines} className="pr-hud-lines" aria-hidden="true">
        {tags.map((t, i) => (
          <path key={`p${i}`} className={t.note ? 'is-note' : ''} />
        ))}
        {tags.map((t, i) => (
          <rect key={`r${i}`} width="8" height="8" className={t.note ? 'is-note' : ''} />
        ))}
      </svg>
      <div ref={hud} className="pr-hud" aria-hidden="true">
        {tags.map((t, i) => (
          <div key={`${props.skin}-${i}-${t.note ?? ''}`} className={`pr-tag${t.note ? ' is-note' : ''}`}>
            <span className="pr-tag-name">{t.note ? noteLabel(t.note) : `${t.part.name}-${(1204 + i * 37).toString()}`}</span>
            <span className="pr-tag-sub" data-coord />
          </div>
        ))}
      </div>
    </div>
  )
}

/** A note path as the HUD names it: its folder as the kind, its name in capitals. */
function noteLabel(path: string): string {
  const parts = path.split('/')
  const name = parts.pop() ?? path
  const kind = parts[0] ?? 'NOTE'
  return `${kind.toUpperCase()} · ${name.replace(/[-_]/g, ' ')}`
}
