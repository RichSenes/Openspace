import { useEffect, useMemo, useRef, useState } from 'react'
import '../../lab.css'
import '../iso.css'
import { planLab } from './furnish'
import type { Flavor, OfficeSpec } from './layout'
import { hourNow, type Weather } from './sky'
import { makeLab } from './world'

/**
 * `?lab=world`: the lab made from a setup, filling the screen, always in its middle. Change the
 * offices (how many agents, what kind of work) and the lab is planned, furnished and peopled
 * again; the city round it reaches as far as the screen does. The zoom picks itself so the lab
 * fills most of the screen; the hour and the weather change the light outside.
 */

const DEMO: OfficeSpec[] = [
  { name: 'Karuta', accent: '#5b9bff', agents: 5, flavor: 'code' },
  { name: 'Openspace', accent: '#f2a948', agents: 3, flavor: 'code' },
  { name: 'Riku', accent: '#3fd29a', agents: 2, flavor: 'research' },
  { name: 'Atelier', accent: '#ff7aa8', agents: 3, flavor: 'creative' },
]

const FLAVORS: Flavor[] = ['code', 'research', 'creative', 'data']
const WEATHERS: Weather[] = ['clear', 'cloudy', 'rain', 'storm']
const ACCENTS = ['#5b9bff', '#f2a948', '#3fd29a', '#ff7aa8', '#c48bff', '#e6d25a', '#5fd7e6', '#ff8a5c']
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
  const [offices, setOffices] = useState(DEMO)
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
  const scale = zoom || auto
  const lab = useMemo(() => makeLab(offices, Math.ceil(vw / scale), Math.ceil(vh / scale)), [offices, vw, vh, scale])
  const ref = useRef<HTMLCanvasElement>(null)
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
      lab.draw(ctx, (now - start) / 1000, dt, when.current)
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
        width={lab.world.width}
        height={lab.world.height}
        className="iso-canvas world-canvas"
        style={{ width: lab.world.width * scale, height: lab.world.height * scale }}
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
              {[0, 1, 2, 3].map((z) => (
                <button key={z} type="button" aria-pressed={zoom === z} onClick={() => setZoom(z)}>
                  {z === 0 ? `Auto (×${auto})` : `×${z}`}
                </button>
              ))}
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
                    <input type="number" min={1} max={12} value={o.agents} onChange={(e) => setOffice(i, { agents: Math.max(1, Math.min(12, Number(e.target.value) || 1)) })} />
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
          </>
        )}
      </aside>
    </main>
  )
}
