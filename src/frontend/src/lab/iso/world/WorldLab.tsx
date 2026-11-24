import { useEffect, useMemo, useRef, useState } from 'react'
import '../../lab.css'
import '../iso.css'
import type { Flavor, OfficeSpec } from './layout'
import { hourNow, type Weather } from './sky'
import { makeLab } from './world'

/**
 * `?lab=world`: the lab made from a setup, not by hand. Change the offices (how many agents,
 * what kind of work) and the building is planned, furnished and peopled again; the hour and
 * the weather change the light outside; the lamps can be switched off.
 */

const DEMO: OfficeSpec[] = [
  { name: 'Karuta', accent: '#5b9bff', agents: 5, flavor: 'code' },
  { name: 'Openspace', accent: '#f2a948', agents: 3, flavor: 'code' },
  { name: 'Riku', accent: '#3fd29a', agents: 2, flavor: 'research' },
  { name: 'Atelier', accent: '#ff7aa8', agents: 3, flavor: 'creative' },
]

const FLAVORS: Flavor[] = ['code', 'research', 'creative', 'data']
const ACCENTS = ['#5b9bff', '#f2a948', '#3fd29a', '#ff7aa8', '#c48bff', '#e6d25a', '#5fd7e6', '#ff8a5c']
const NAMES = ['Kumo', 'Hoshi', 'Mizu', 'Kaze', 'Tsuki', 'Yama']
const WEATHERS: Weather[] = ['clear', 'cloudy', 'rain', 'storm']

/** The page can open on a given hour, weather and lamps: ?lab=world&hour=22&weather=storm&lamps=off. */
const ASKED = new URLSearchParams(location.search)

export function WorldLab() {
  const [offices, setOffices] = useState(DEMO)
  const [live, setLive] = useState(!ASKED.has('hour'))
  const [hour, setHour] = useState(() => (ASKED.has('hour') ? Number(ASKED.get('hour')) : hourNow()))
  const [weather, setWeather] = useState<Weather>(() => (WEATHERS.includes(ASKED.get('weather') as Weather) ? (ASKED.get('weather') as Weather) : 'clear'))
  const [lamps, setLamps] = useState(ASKED.get('lamps') !== 'off')
  const [zoom, setZoom] = useState(1)
  const lab = useMemo(() => makeLab(offices), [offices])
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
    <main className="lab iso-lab">
      <header className="lab-head">
        <h1>Lab · the whole building</h1>
        <div className="iso-tools" role="group" aria-label="Zoom">
          {[1, 2].map((z) => (
            <button key={z} type="button" aria-pressed={zoom === z} onClick={() => setZoom(z)}>
              ×{z}
            </button>
          ))}
          <button type="button" aria-pressed={!lamps} onClick={() => setLamps((l) => !l)}>
            Lamps off
          </button>
        </div>
      </header>

      <section className="world-controls" aria-label="Time and weather">
        <label>
          <input type="checkbox" checked={live} onChange={(e) => setLive(e.target.checked)} /> Now
        </label>
        <label className="world-hour">
          Hour {String(hh).padStart(2, '0')}:{String(mm).padStart(2, '0')}
          <input
            type="range"
            min={0}
            max={23.99}
            step={0.05}
            value={hour}
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
      </section>

      <section className="world-offices" aria-label="Offices">
        {offices.map((o, i) => (
          <fieldset key={i}>
            <legend style={{ color: o.accent }}>{o.name}</legend>
            <label>
              Name
              <input value={o.name} maxLength={16} size={9} onChange={(e) => setOffice(i, { name: e.target.value || '?' })} />
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
      </section>

      <div className="iso-frame world-frame">
        <canvas
          ref={ref}
          width={lab.world.width}
          height={lab.world.height}
          className="iso-canvas"
          style={{ width: lab.world.width * zoom, height: lab.world.height * zoom }}
          role="img"
          aria-label="The lab made from the offices: Shiori's hall in the middle, a room for each office, a lounge, the grounds around"
        />
      </div>
    </main>
  )
}
