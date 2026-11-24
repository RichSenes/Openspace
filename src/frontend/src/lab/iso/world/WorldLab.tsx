import { useEffect, useMemo, useRef, useState } from 'react'
import '../../lab.css'
import '../iso.css'
import { useWorld } from '../../../api/hooks'
import { ACCENTS, specsOf } from './fromWorld'
import { planLab } from './furnish'
import type { Flavor, OfficeSpec } from './layout'
import { hourNow, type Weather } from './sky'
import { makeLab, WORLD_H, WORLD_W } from './world'

/**
 * `?lab=world`: the lab made from a setup, filling the screen, always in its middle. Change the
 * offices (how many agents, what kind of work) and the lab is planned, furnished and peopled
 * again; the city round it reaches as far as the screen does. The zoom picks itself so the lab
 * fills most of the screen; the hour and the weather change the light outside.
 */

const DEMO: OfficeSpec[] = [
  { name: 'Karuta', accent: '#5b9bff', agents: 17, flavor: 'code' },
  { name: 'Openspace', accent: '#f2a948', agents: 3, flavor: 'code' },
  { name: 'Riku', accent: '#3fd29a', agents: 2, flavor: 'research' },
  { name: 'Atelier', accent: '#ff7aa8', agents: 3, flavor: 'creative' },
]

const FLAVORS: Flavor[] = ['code', 'research', 'creative', 'data']
const WEATHERS: Weather[] = ['clear', 'cloudy', 'rain', 'storm']
const NAMES = ['Kumo', 'Hoshi', 'Mizu', 'Kaze', 'Tsuki', 'Yama']

/** The page can open on a given hour, weather, lamps and zoom: ?lab=world&hour=22&weather=storm&lamps=off&zoom=2. */
const ASKED = new URLSearchParams(location.search)

function useViewport() {
  const [size, setSize] = useState(() => [window.innerWidth, window.innerHeight] as const)
  useEffect(() => {
    let id = 0
    const on = () => {
      clearTimeout(id)
      id = window.setTimeout(() => setSize([window.innerWidth, window.innerHeight]), 250)
    }
    addEventListener('resize', on)
    return () => removeEventListener('resize', on)
  }, [])
  return size
}

export function WorldLab() {
  const [demo, setDemo] = useState(DEMO)
  // The real offices, when Openspace's API answers and the page is asked for them (?lab=world&real).
  const [real, setReal] = useState(ASKED.has('real'))
  const world = useWorld()
  // The lab follows the setup, not every change of state: rebuilt when offices or their seats change.
  const realKey = world.data ? JSON.stringify(specsOf(world.data)) : ''
  const offices = useMemo(() => (real && realKey ? (JSON.parse(realKey) as OfficeSpec[]) : demo), [real, realKey, demo])
  const setOffices = setDemo
  const [live, setLive] = useState(!ASKED.has('hour'))
  const [hour, setHour] = useState(() => (ASKED.has('hour') ? Number(ASKED.get('hour')) : hourNow()))
  const [weather, setWeather] = useState<Weather>(() => (WEATHERS.includes(ASKED.get('weather') as Weather) ? (ASKED.get('weather') as Weather) : 'clear'))
  const [lamps, setLamps] = useState(ASKED.get('lamps') !== 'off')
  const [zoom, setZoom] = useState(Number(ASKED.get('zoom')) || 0)
  const [open, setOpen] = useState(false)
  const [vw, vh] = useViewport()
  // The zoom that makes the lab fill most of the screen, unless one is chosen.
  const auto = useMemo(() => {
    const box = planLab(offices).plan.box
    const span = (box[2] - box[0] + box[3] - box[1]) * 8
    const labW = span * 2
    const labH = span * (4 / 3) * 0.5 + 120
    return Math.max(1, Math.min(4, Math.floor(Math.min((vw * 0.92) / labW, (vh * 0.92) / labH))))
  }, [offices, vw, vh])
  // Never zoomed out past the world's edges: the view always fits in what is painted.
  const least = Math.max(1, Math.ceil(Math.max(vw / WORLD_W, vh / WORLD_H)))
  const scale = Math.min(6, Math.max(least, zoom || auto))
  // Painted once per setup; the page's size only moves the window onto it.
  // With real offices the characters are the real ones: built once per setup, then moved by every change.
  const lab = useMemo(() => makeLab(offices, real ? { offices: [], waiting: [] } : undefined), [offices, real])
  useEffect(() => {
    if (real && world.data) lab.update(world.data)
  }, [lab, real, world.data])
  const cw = Math.ceil(vw / scale)
  const ch = Math.ceil(vh / scale)
  const ref = useRef<HTMLCanvasElement>(null)
  // Where the view looks, in world pixels: the lab until it is moved.
  const centre = useRef<[number, number] | null>(null)
  const view = useRef({ scale, cw, ch, vw, vh })
  useEffect(() => {
    view.current = { scale, cw, ch, vw, vh }
  }, [scale, cw, ch, vw, vh])
  const clampCentre = (c: [number, number], w = view.current.cw, h = view.current.ch): [number, number] => [
    Math.min(WORLD_W - w / 2, Math.max(w / 2, c[0])),
    Math.min(WORLD_H - h / 2, Math.max(h / 2, c[1])),
  ]

  // The wheel (or a trackpad's pinch) zooms on the point under the pointer; dragging moves the view.
  useEffect(() => {
    const canvas = ref.current!
    let lastWheel = 0
    const onWheel = (e: WheelEvent) => {
      e.preventDefault()
      const now = performance.now()
      if (now - lastWheel < 110 || Math.abs(e.deltaY) < 1) return
      lastWheel = now
      const { scale: s, vw: w, vh: h } = view.current
      const next = Math.min(6, Math.max(Math.max(1, Math.ceil(Math.max(w / WORLD_W, h / WORLD_H))), s + (e.deltaY < 0 ? 1 : -1)))
      if (next === s) return
      const c = centre.current ?? [WORLD_W / 2, WORLD_H / 2]
      const px = e.clientX - w / 2
      const py = e.clientY - h / 2
      const at: [number, number] = [c[0] + px / s, c[1] + py / s]
      centre.current = clampCentre([at[0] - px / next, at[1] - py / next], Math.ceil(w / next), Math.ceil(h / next))
      setZoom(next)
    }
    let drag: { x: number; y: number } | null = null
    const onDown = (e: PointerEvent) => {
      drag = { x: e.clientX, y: e.clientY }
      canvas.setPointerCapture(e.pointerId)
    }
    const onMove = (e: PointerEvent) => {
      if (!drag) return
      const { scale: s } = view.current
      const c = centre.current ?? [WORLD_W / 2, WORLD_H / 2]
      centre.current = clampCentre([c[0] - (e.clientX - drag.x) / s, c[1] - (e.clientY - drag.y) / s])
      drag = { x: e.clientX, y: e.clientY }
    }
    const onUp = () => (drag = null)
    canvas.addEventListener('wheel', onWheel, { passive: false })
    canvas.addEventListener('pointerdown', onDown)
    canvas.addEventListener('pointermove', onMove)
    canvas.addEventListener('pointerup', onUp)
    canvas.addEventListener('pointercancel', onUp)
    return () => {
      canvas.removeEventListener('wheel', onWheel)
      canvas.removeEventListener('pointerdown', onDown)
      canvas.removeEventListener('pointermove', onMove)
      canvas.removeEventListener('pointerup', onUp)
      canvas.removeEventListener('pointercancel', onUp)
    }
  }, [])
  const when = useRef({ hour, weather, lamps })
  useEffect(() => {
    when.current = { hour, weather, lamps }
  }, [hour, weather, lamps])

  useEffect(() => {
    if (!live) return
    const id = setInterval(() => setHour(hourNow()), 30_000)
    return () => clearInterval(id)
  }, [live])

  useEffect(() => {
    const ctx = ref.current!.getContext('2d')!
    let raf = 0
    let last = performance.now()
    const start = last
    const loop = (now: number) => {
      const dt = Math.min(0.1, (now - last) / 1000)
      last = now
      lab.draw(ctx, (now - start) / 1000, dt, when.current, ctx.canvas.width, ctx.canvas.height, centre.current ?? undefined)
      raf = requestAnimationFrame(loop)
    }
    raf = requestAnimationFrame(loop)
    return () => cancelAnimationFrame(raf)
  }, [lab])

  const setOffice = (i: number, patch: Partial<OfficeSpec>) => setOffices((list) => list.map((o, k) => (k === i ? { ...o, ...patch } : o)))
  const hh = Math.floor(hour)
  const mm = Math.floor((hour - hh) * 60)

  return (
    <main className="world-page">
      <canvas
        ref={ref}
        width={cw}
        height={ch}
        className="iso-canvas world-canvas"
        style={{ width: cw * scale, height: ch * scale }}
        role="img"
        aria-label="The lab made from the offices, in the middle of a city: Shiori's area at its heart, an area for each office round her, a lounge"
      />
      <aside className="world-panel" aria-label="Lab settings">
        <div className="world-panel-head">
          <h1>Lab</h1>
          <button type="button" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
            {open ? 'Hide' : 'Settings'}
          </button>
        </div>
        {open && (
          <>
            <div className="iso-tools" role="group" aria-label="Zoom">
              <button
                type="button"
                aria-pressed={zoom === 0}
                onClick={() => {
                  centre.current = null
                  setZoom(0)
                }}
              >
                Lab in view (×{Math.max(least, auto)})
              </button>
              {[1, 2, 3, 4].map((z) => (
                <button key={z} type="button" aria-pressed={zoom === z} disabled={z < least} onClick={() => setZoom(z)}>
                  ×{z}
                </button>
              ))}
              {world.data && (
                <button type="button" aria-pressed={real} onClick={() => setReal((r) => !r)}>
                  Real offices
                </button>
              )}
              <button type="button" aria-pressed={!lamps} onClick={() => setLamps((l) => !l)}>
                Lamps off
              </button>
            </div>
            <div className="world-controls">
              <label>
                <input type="checkbox" checked={live} onChange={(e) => setLive(e.target.checked)} /> Now
              </label>
              <label className="world-hour">
                {String(hh).padStart(2, '0')}:{String(mm).padStart(2, '0')}
                <input
                  type="range"
                  min={0}
                  max={23.99}
                  step={0.05}
                  value={hour}
                  aria-label="Hour"
                  onChange={(e) => {
                    setLive(false)
                    setHour(Number(e.target.value))
                  }}
                />
              </label>
              <label>
                Weather
                <select value={weather} onChange={(e) => setWeather(e.target.value as Weather)}>
                  {WEATHERS.map((w) => (
                    <option key={w}>{w}</option>
                  ))}
                </select>
              </label>
            </div>
            {real ? (
              <p className="world-note">The offices set up in Openspace: an area per office, a seat for each desk and each routine.</p>
            ) : (
              <div className="world-offices">
                {offices.map((o, i) => (
                  <fieldset key={i}>
                    <legend style={{ color: o.accent }}>{o.name}</legend>
                    <label>
                      Name
                      <input value={o.name} maxLength={16} size={8} onChange={(e) => setOffice(i, { name: e.target.value || '?' })} />
                    </label>
                    <label>
                      Agents
                      <input type="number" min={1} max={40} value={o.agents} onChange={(e) => setOffice(i, { agents: Math.max(1, Math.min(40, Number(e.target.value) || 1)) })} />
                    </label>
                    <label>
                      Work
                      <select value={o.flavor} onChange={(e) => setOffice(i, { flavor: e.target.value as Flavor })}>
                        {FLAVORS.map((f) => (
                          <option key={f}>{f}</option>
                        ))}
                      </select>
                    </label>
                    {offices.length > 1 && (
                      <button type="button" onClick={() => setOffices((list) => list.filter((_, k) => k !== i))}>
                        Remove
                      </button>
                    )}
                  </fieldset>
                ))}
                {offices.length < 8 && (
                  <button
                    type="button"
                    onClick={() =>
                      setOffices((list) => [
                        ...list,
                        {
                          name: NAMES[list.length % NAMES.length] + (list.length >= NAMES.length ? list.length : ''),
                          accent: ACCENTS[list.length % ACCENTS.length],
                          agents: 2,
                          flavor: FLAVORS[list.length % 4],
                        },
                      ])
                    }
                  >
                    Add an office
                  </button>
                )}
              </div>
            )}
          </>
        )}
      </aside>
    </main>
  )
}
