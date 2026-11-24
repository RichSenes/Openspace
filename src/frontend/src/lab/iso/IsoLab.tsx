import { useEffect, useMemo, useRef, useState } from 'react'
import '../lab.css'
import './iso.css'
import { makeBrain } from './brain'
import { benchDesk, capsule, coffeeCounter, cornerDesk, glassWall, holoTable, microscopeBench, plant, rack, sofa, totem, whiteboard, type Prop } from './props'
import { makeScene } from './scene'
import { SCENE_H, SCENE_W } from './room'
import { FOOT_X, FOOT_Y, scientistFrames, type Facing, type Look, type Pose } from './scientist'

/**
 * `?lab=iso`: a study of Openspace as an isometric lab, Habbo's angle with dense pixel art.
 * The live scene first, then the scientists in every direction and pose, then each piece alone.
 */

const LOOKS: Look[] = [
  { skin: '#f1c9a5', hair: '#1d1a22', style: 'short', shirt: '#5b9bff', glasses: true },
  { skin: '#e8b48f', hair: '#5a3424', style: 'long', shirt: '#3fd29a' },
  { skin: '#8d5a3b', hair: '#141018', style: 'curly', shirt: '#f2a948' },
  { skin: '#f6d7c3', hair: '#c9a06a', style: 'bun', shirt: '#5b9bff' },
  { skin: '#c98e66', hair: '#2a2a33', style: 'pony', shirt: '#3fd29a' },
]

const CELLS: { facing: Facing; pose: Pose; label: string }[] = [
  { facing: 'SE', pose: 'walk', label: 'walk ↘' },
  { facing: 'SW', pose: 'walk', label: 'walk ↙' },
  { facing: 'NE', pose: 'walk', label: 'walk ↗' },
  { facing: 'NW', pose: 'walk', label: 'walk ↖' },
  { facing: 'SE', pose: 'notes', label: 'notes' },
  { facing: 'SE', pose: 'think', label: 'think' },
  { facing: 'SE', pose: 'coffee', label: 'coffee' },
  { facing: 'SE', pose: 'type', label: 'type' },
  { facing: 'NE', pose: 'type', label: 'type, back' },
]

const CELL_W = 34
const CELL_H = 60

type Draw = (ctx: CanvasRenderingContext2D, t: number, dt: number) => void

/** A pixel canvas shown at a whole-number scale, painted every frame. */
function PixelCanvas({ width, height, scale, draw, label }: { width: number; height: number; scale: number; draw: Draw; label: string }) {
  const ref = useRef<HTMLCanvasElement>(null)
  const paint = useRef(draw)
  useEffect(() => {
    paint.current = draw
  })
  useEffect(() => {
    const ctx = ref.current!.getContext('2d')!
    ctx.imageSmoothingEnabled = false
    let raf = 0
    let last = performance.now()
    const start = last
    const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches
    const loop = (now: number) => {
      const dt = Math.min(0.1, (now - last) / 1000)
      last = now
      paint.current(ctx, reduce ? 4 : (now - start) / 1000, reduce ? 0 : dt)
      if (!reduce) raf = requestAnimationFrame(loop)
    }
    raf = requestAnimationFrame(loop)
    return () => cancelAnimationFrame(raf)
  }, [])
  return <canvas ref={ref} width={width} height={height} className="iso-canvas" style={{ width: width * scale, height: height * scale }} role="img" aria-label={label} />
}

function useScale(base: number, width: number): number {
  const [fit, setFit] = useState(() => Math.max(1, Math.floor((window.innerWidth - 48) / width)))
  useEffect(() => {
    const on = () => setFit(Math.max(1, Math.floor((window.innerWidth - 48) / width)))
    addEventListener('resize', on)
    return () => removeEventListener('resize', on)
  }, [width])
  return base === 0 ? fit : base
}

export function IsoLab() {
  const [zoom, setZoom] = useState(0)
  const [paused, setPaused] = useState(false)
  const [night, setNight] = useState(() => new URLSearchParams(location.search).has('night'))
  const scene = useMemo(() => makeScene(), [])
  const sceneScale = useScale(zoom, SCENE_W)
  const clock = useRef({ t: 0 })

  const drawScene: Draw = (ctx, _t, dt) => {
    if (!paused) clock.current.t += dt
    scene.draw(ctx, clock.current.t, paused ? 0 : dt, night)
  }

  const frames = useMemo(() => LOOKS.map(scientistFrames), [])
  const sheetW = CELL_W * CELLS.length
  const sheetH = CELL_H * LOOKS.length
  const drawPeople: Draw = (ctx, t) => {
    ctx.fillStyle = '#d6dde4'
    ctx.fillRect(0, 0, sheetW, sheetH)
    LOOKS.forEach((_, row) => {
      CELLS.forEach((cell, col) => {
        if ((row + col) % 2) {
          ctx.fillStyle = '#cfd7df'
          ctx.fillRect(col * CELL_W, row * CELL_H, CELL_W, CELL_H)
        }
        const frame = cell.pose === 'walk' ? Math.floor(t * 8) : Math.floor(t * (cell.pose === 'coffee' ? 0.7 : 3))
        const sprite = frames[row].get(cell.facing, cell.pose, frame)
        const x = col * CELL_W + CELL_W / 2
        const y = row * CELL_H + CELL_H - 3
        ctx.globalAlpha = 0.2
        ctx.fillStyle = '#0b1220'
        ctx.fillRect(x - 6, y - 1, 13, 3)
        ctx.globalAlpha = 1
        ctx.drawImage(sprite, x - FOOT_X, y - FOOT_Y)
      })
    })
  }

  const pieces = useMemo(() => {
    const list: { name: string; prop: Prop }[] = [
      { name: 'Capsule', prop: capsule('#37d6a0', 3) },
      { name: 'Capsule', prop: capsule('#b98cff', 12) },
      { name: 'Server rack', prop: rack(4) },
      { name: 'Bench desk', prop: benchDesk('#5b9bff', 1) },
      { name: 'Corner desk', prop: cornerDesk('#f2a948', 2) },
      { name: 'Microscope bench', prop: microscopeBench('#3fd29a') },
      { name: 'Hologram table', prop: holoTable('#5b9bff') },
      { name: 'Whiteboard, maths', prop: whiteboard('math', 5) },
      { name: 'Whiteboard, diagram', prop: whiteboard('diagram', 7) },
      { name: 'Whiteboard, chart', prop: whiteboard('graph', 9) },
      { name: 'Live totem', prop: totem('#f2a948') },
      { name: 'Coffee', prop: coffeeCounter('#c48bff') },
      { name: 'Sofa', prop: sofa('#6c7fa8') },
      { name: 'Glass wall', prop: glassWall(20) },
      { name: 'Plant', prop: plant(6) },
    ]
    return list
  }, [])
  const brain = useMemo(() => makeBrain(), [])

  return (
    <main className="lab iso-lab">
      <header className="lab-head">
        <h1>Lab · isometric study</h1>
        <div className="iso-tools" role="group" aria-label="Zoom">
          {[0, 1, 2, 3].map((z) => (
            <button key={z} type="button" aria-pressed={zoom === z} onClick={() => setZoom(z)}>
              {z === 0 ? 'Fit' : `×${z}`}
            </button>
          ))}
          <button type="button" aria-pressed={night} onClick={() => setNight((n) => !n)}>
            Night
          </button>
          <button type="button" aria-pressed={paused} onClick={() => setPaused((p) => !p)}>
            {paused ? 'Play' : 'Pause'}
          </button>
        </div>
      </header>

      <section>
        <h2>The lab</h2>
        <p className="iso-note">Clean sci-fi, cold light, the city burning outside. Each former office is an area of the open space with its own floor and colour; Shiori is the brain under the dome.</p>
        <div className="iso-frame">
          <PixelCanvas width={SCENE_W} height={SCENE_H} scale={sceneScale} draw={drawScene} label="An isometric lab: scientists at work around a huge brain under a glass dome" />
        </div>
      </section>

      <section>
        <h2>Shiori</h2>
        <div className="iso-frame">
          <PixelCanvas
            width={220}
            height={250}
            scale={2}
            label="The brain under its dome, particles orbiting it"
            draw={(ctx, t) => {
              ctx.fillStyle = '#d2d9e1'
              ctx.fillRect(0, 0, 220, 250)
              brain.draw(ctx, 110, 205, t)
            }}
          />
        </div>
      </section>

      <section>
        <h2>Scientists</h2>
        <p className="iso-note">Two drawings per pose, front and back; flipped, they make the other two directions. Hair, skin and the shirt under the coat (the colour of their area) make each one somebody.</p>
        <div className="iso-frame">
          <PixelCanvas width={sheetW} height={sheetH} scale={3} draw={drawPeople} label="Five scientists walking in four directions and in their poses" />
        </div>
        <p className="iso-legend">{CELLS.map((c) => c.label).join(' · ')}</p>
      </section>

      <section>
        <h2>Pieces</h2>
        <div className="iso-pieces">
          {pieces.map(({ name, prop }, i) => {
            const c = prop.sprite.canvas
            return (
              <figure key={i}>
                <PixelCanvas
                  width={c.width + 8}
                  height={c.height + 8}
                  scale={2}
                  label={name}
                  draw={(ctx, t) => {
                    ctx.fillStyle = '#d2d9e1'
                    ctx.fillRect(0, 0, c.width + 8, c.height + 8)
                    ctx.drawImage(c, 4, 4)
                    prop.anim?.(ctx, 4 + prop.sprite.ax, 4 + prop.sprite.ay, t, 1)
                  }}
                />
                <figcaption>{name}</figcaption>
              </figure>
            )
          })}
        </div>
      </section>
    </main>
  )
}
