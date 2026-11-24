import { useEffect, useMemo, useRef, useState } from 'react'
import '../../lab.css'
import '../iso.css'
import { useWorld } from '../../../api/hooks'
import { ACCENTS, specsOf } from './fromWorld'
import { planLab } from './furnish'
import type { Flavor, OfficeSpec } from './layout'
import { hourNow, type Weather } from './sky'
import { Card } from './Cards'
import { ShioriPanel, WakeListener } from './ShioriPanel'
import type { Heard } from '../../../shiori/voice'
import { loadByName } from '../../../shiori/wake'
import { makeLab, WORLD_H, WORLD_W, type Picked } from './world'

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
  // The card open over the lab: a character's or Shiori's.
  const [picked, setPicked] = useState<Picked | null>(null)
  // Shiori's panel, and what was said to her by name before it opened.
  const [shiori, setShiori] = useState(false)
  const [said, setSaid] = useState<Heard | null>(null)
  // Listening for her name, shared by the lab (panel closed) and her panel (open).
  const [byName, setByName] = useState(loadByName)
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
  const least = Math.max(1, vw / WORLD_W, vh / WORLD_H)
  // Painted once per setup; the page's size only moves the window onto it.
  // With real offices the characters are the real ones: built once per setup, then moved by every change.
  const lab = useMemo(() => makeLab(offices, real ? { offices: [], waiting: [] } : undefined), [offices, real])
  useEffect(() => {
    if (real && world.data) lab.update(world.data)
  }, [lab, real, world.data])
  const ref = useRef<HTMLCanvasElement>(null)
  const labRef = useRef(lab)
  useEffect(() => {
    labRef.current = lab
  }, [lab])
  useEffect(() => {
    lab.select(picked?.kind === 'agent' ? picked.id : null)
  }, [lab, picked])
  // The camera: where it looks (world pixels, the lab until moved) and how close, gliding to
  // where it is asked to be, as photos zoom; the point under the pointer stays put.
  const cam = useRef({ centre: null as [number, number] | null, scale: 0, target: 0, anchor: [0, 0] as [number, number] })
  const screen = useRef({ vw, vh, least, auto })
  useEffect(() => {
    screen.current = { vw, vh, least, auto }
    // A chosen zoom, or the lab's own; never past the world's edges.
    cam.current.target = Math.min(6, Math.max(least, zoom || auto))
    if (!cam.current.scale) cam.current.scale = cam.current.target
  }, [vw, vh, least, auto, zoom])
  const clampCentre = (c: [number, number], s: number): [number, number] => {
    const { vw: w, vh: h } = screen.current
    const cw = w / s
    const ch = h / s
    return [Math.min(WORLD_W - cw / 2, Math.max(cw / 2, c[0])), Math.min(WORLD_H - ch / 2, Math.max(ch / 2, c[1]))]
  }

  // The wheel (or a trackpad's pinch) zooms smoothly on the point under the pointer; dragging moves the view.
  useEffect(() => {
    const canvas = ref.current!
    const onWheel = (e: WheelEvent) => {
      e.preventDefault()
      const c = cam.current
      const { least: l } = screen.current
      c.target = Math.min(6, Math.max(l, c.target * Math.exp(-e.deltaY * (e.ctrlKey ? 0.01 : 0.0016))))
      c.anchor = [e.clientX, e.clientY]
      setZoom(c.target)
    }
    let drag: { x: number; y: number } | null = null
    let moved = 0
    const onDown = (e: PointerEvent) => {
      drag = { x: e.clientX, y: e.clientY }
      moved = 0
      canvas.setPointerCapture(e.pointerId)
    }
    const onMove = (e: PointerEvent) => {
      if (!drag) return
      const c = cam.current
      const at = c.centre ?? [WORLD_W / 2, WORLD_H / 2]
      c.centre = clampCentre([at[0] - (e.clientX - drag.x) / c.scale, at[1] - (e.clientY - drag.y) / c.scale], c.scale)
      moved += Math.abs(e.clientX - drag.x) + Math.abs(e.clientY - drag.y)
      drag = { x: e.clientX, y: e.clientY }
    }
    const onUp = (e: PointerEvent) => {
      // A click, not a drag: whoever is under the pointer opens their card.
      if (drag && moved < 5) {
        const found = labRef.current.hit(e.clientX / cam.current.scale, e.clientY / cam.current.scale)
        if (found?.kind === 'shiori') {
          setPicked(null)
          setShiori(true)
        } else setPicked(found)
      }
      drag = null
    }
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
      // The zoom glides towards its target, keeping the point under the pointer where it is.
      const c = cam.current
      const { vw: w, vh: h } = screen.current
      if (Math.abs(c.target - c.scale) > 0.002) {
        const next = c.scale + (c.target - c.scale) * Math.min(1, dt * 12)
        const at = c.centre ?? [WORLD_W / 2, WORLD_H / 2]
        const px = c.anchor[0] - w / 2
        const py = c.anchor[1] - h / 2
        c.centre = clampCentre([at[0] + px / c.scale - px / next, at[1] + py / c.scale - py / next], next)
        c.scale = next
      }
      // The canvas holds the view's world pixels; the page shows it at the zoom, pixels kept sharp.
      const cw = Math.ceil(w / c.scale)
      const ch = Math.ceil(h / c.scale)
      if (ctx.canvas.width !== cw || ctx.canvas.height !== ch) {
        ctx.canvas.width = cw
        ctx.canvas.height = ch
      }
      ctx.canvas.style.width = `${cw * c.scale}px`
      ctx.canvas.style.height = `${ch * c.scale}px`
      lab.draw(ctx, (now - start) / 1000, dt, when.current, cw, ch, c.centre ?? undefined)
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
        className="iso-canvas world-canvas"
        role="img"
        aria-label="The lab made from the offices, in the middle of a city: Shiori's area at its heart, an area for each office round her, a lounge"
      />
      {picked && <Card picked={picked} world={world.data} onClose={() => setPicked(null)} />}
      <ShioriPanel open={shiori} onClose={() => setShiori(false)} world={world.data} pending={said} onPendingTaken={() => setSaid(null)} byName={byName} onByName={setByName} />
      {!shiori && (
        <WakeListener
          on={byName}
          onCalled={(heard) => {
            setSaid(heard)
            setPicked(null)
            setShiori(true)
          }}
        />
      )}
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
                  cam.current.centre = null
                  cam.current.anchor = [vw / 2, vh / 2]
                  setZoom(0)
                }}
              >
                Lab in view
              </button>
              {[1, 2, 3, 4].map((z) => (
                <button
                  key={z}
                  type="button"
                  aria-pressed={Math.abs(zoom - z) < 0.05}
                  disabled={z < least}
                  onClick={() => {
                    cam.current.anchor = [vw / 2, vh / 2]
                    setZoom(z)
                  }}
                >
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
