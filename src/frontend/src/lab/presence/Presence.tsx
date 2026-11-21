import { MessagesSquare, Mic, Pause, Play, RotateCcw, Send, SlidersHorizontal, StickyNote, X } from 'lucide-react'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { boardSections } from '../shiori/board'
import { FACE_LOOKS, FACE_STATES, type FaceState } from '../shiori/faceMachine'
import { useReducedMotion } from '../shiori/faces/useFaceLoop'
import { MODE_ORDER, MODES, type Mode } from '../shiori/modes'
import { buildTimeline, SESSION, snapshot, timeOf, type ManualSwitch, type Token } from '../shiori/script'
import { RIGS } from './body/rig'
import type { SkinId } from './body/shapes'
import { ShioriBody } from './body/ShioriBody'
import { focusFor, subtitles } from './subtitles'
import { useWeather } from '../../api/hooks'
import type { Sky } from '../../api/types'
import { HOURS, hourOf, SKIES, type Hour, type WorldId } from './world/types'
import { WorldBackdrop } from './world/WorldBackdrop'
import { WORLD_ORDER, WORLDS } from './world/worlds'
import './presence.css'

const SKINS: SkinId[] = ['swarm', 'medusa', 'portrait']
const STORE = 'openspace.lab.presence'
const TICK = 50

interface Prefs {
  skin: SkinId
  pixel: number
  hour: Hour | 'auto'
  /** 'auto' follows the real weather of the person's town. */
  sky: Sky | 'auto'
  world: WorldId
}

const DEFAULTS: Prefs = { skin: 'swarm', pixel: 3, hour: 'auto', sky: 'auto', world: 'city' }

function loadPrefs(): Prefs {
  try {
    const v = JSON.parse(localStorage.getItem(STORE) ?? '{}') as Partial<Prefs>
    return {
      skin: SKINS.includes(v.skin as SkinId) ? (v.skin as SkinId) : DEFAULTS.skin,
      pixel: [0, 2, 3, 4].includes(v.pixel ?? -1) ? v.pixel! : DEFAULTS.pixel,
      hour: v.hour === 'auto' || HOURS.includes(v.hour as Hour) ? (v.hour as Hour | 'auto') : DEFAULTS.hour,
      sky: v.sky === 'auto' || SKIES.includes(v.sky as Sky) ? (v.sky as Sky | 'auto') : DEFAULTS.sky,
      world: WORLD_ORDER.includes(v.world as WorldId) ? (v.world as WorldId) : DEFAULTS.world,
    }
  } catch {
    return DEFAULTS
  }
}

function savePrefs(p: Prefs) {
  try {
    localStorage.setItem(STORE, JSON.stringify(p))
  } catch {
    // Private windows may refuse storage: the choice still holds for this visit.
  }
}

function Words({ tokens, streaming }: { tokens: Token[]; streaming: boolean }) {
  return (
    <>
      {tokens.map((t, i) => (
        <span key={i} className={i === tokens.length - 1 && streaming ? 'pr-word is-new' : 'pr-word'}>
          {i > 0 && !t.glued ? ' ' : ''}
          {t.type === 'cite' ? <span className="pr-cite">{t.path.split('/').pop()}</span> : t.text}
        </span>
      ))}
      {streaming && <span className="pr-caret" aria-hidden="true" />}
    </>
  )
}

/** `?lab=presence`: Shiori at the centre, in the dystopian world, with subtitles. */
export function Presence() {
  const timeline = useMemo(() => buildTimeline(SESSION), [])
  const [prefs, setPrefs] = useState<Prefs>(loadPrefs)
  const [t, setT] = useState(0)
  const [playing, setPlaying] = useState(true)
  const [manual, setManual] = useState<ManualSwitch[]>([])
  const [forced, setForced] = useState<FaceState | null>(null)
  const [talking, setTalking] = useState(false)
  const [calmPick, setCalm] = useState(false)
  const [drawer, setDrawer] = useState<'log' | 'board' | null>(null)
  const [labOpen, setLabOpen] = useState(() => window.innerWidth >= 1000)
  const [size, setSize] = useState({ w: window.innerWidth, h: window.innerHeight })
  const [clockHour, setClockHour] = useState<Hour>(() => hourOf(new Date()))
  useEffect(() => {
    const id = setInterval(() => setClockHour(hourOf(new Date())), 60_000)
    return () => clearInterval(id)
  }, [])
  const systemCalm = useReducedMotion()
  const calm = systemCalm || calmPick
  const root = useRef<HTMLDivElement>(null)
  const subsRef = useRef<HTMLDivElement>(null)
  const tRef = useRef(t)
  useEffect(() => {
    tRef.current = t
  }, [t])

  useLayoutEffect(() => {
    const el = root.current!
    const ro = new ResizeObserver(() => setSize({ w: el.clientWidth, h: el.clientHeight }))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const snap = useMemo(() => snapshot(timeline, t, manual), [timeline, t, manual])
  const subs = useMemo(() => subtitles(snap.entries), [snap.entries])
  const face: FaceState = talking ? 'listening' : (forced ?? snap.face)
  const running = playing && !snap.done

  useEffect(() => {
    if (!running) return
    const id = setInterval(() => setT((x) => Math.min(timeline.duration, x + TICK)), TICK)
    return () => clearInterval(id)
  }, [running, timeline.duration])

  // A long answer reads as a card that follows its last line.
  useEffect(() => {
    const el = subsRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [subs.shown.length])

  const pick = useCallback((p: Partial<Prefs>) => {
    setPrefs((old) => {
      const next = { ...old, ...p }
      savePrefs(next)
      return next
    })
  }, [])

  const switchMode = useCallback(
    (m: Mode) => setManual((list) => (m === snapshot(timeline, tRef.current, list).mode ? list : [...list, { t: tRef.current, mode: m }])),
    [timeline],
  )

  const restart = () => {
    setT(0)
    setManual([])
    setPlaying(true)
  }

  useEffect(() => {
    const api = {
      skin: (s: SkinId) => pick({ skin: s }),
      pixel: (p: number) => pick({ pixel: p }),
      hour: (h: Hour | 'auto') => pick({ hour: h }),
      sky: (k: Sky | 'auto') => pick({ sky: k }),
      world: (w: WorldId) => pick({ world: w }),
      state: (s: FaceState | null) => setForced(s),
      seek: (step: number | 'end', at: 'start' | 'mid' | 'end' = 'mid') => {
        setPlaying(false)
        setT(step === 'end' ? timeline.duration : timeOf(timeline, step, at))
      },
      play: () => setPlaying(true),
      pause: () => setPlaying(false),
      drawer: (d: 'log' | 'board' | null) => setDrawer(d),
      lab: (open: boolean) => setLabOpen(open),
      calm: (on: boolean) => setCalm(on),
    }
    ;(window as unknown as { __presence: typeof api }).__presence = api
  }, [pick, timeline])

  const focus = focusFor(size.w, size.h, subs.long)
  const info = MODES[snap.mode]
  const look = FACE_LOOKS[face]
  const sections = boardSections(snap.board)
  const progress = Math.round((t / timeline.duration) * 100)
  const realSky = useWeather().data?.now?.sky
  const weather = { hour: prefs.hour === 'auto' ? clockHour : prefs.hour, sky: prefs.sky === 'auto' ? (realSky ?? 'Clear') : prefs.sky }
  const world = WORLDS[prefs.world]

  return (
    <div ref={root} className={`pr${subs.long ? ' is-long' : ''}${calm ? ' is-calm' : ''}`} data-mode={snap.mode} style={{ '--mode': info.hex } as React.CSSProperties}>
      <WorldBackdrop world={prefs.world} pixel={prefs.pixel} calm={calm} weather={weather} />
      <div className="pr-shade" aria-hidden="true" />
      <ShioriBody state={face} skin={prefs.skin} mode={snap.mode} pixel={prefs.pixel} palette={world.palette} calm={calm} focus={focus} notes={subs.notes} />

      <header className="pr-top">
        <div className="pr-brand">
          <span className="pr-mark" aria-hidden="true">
            栞
          </span>
          <span className="pr-brand-text">
            <span className="pr-name">SHIORI</span>
            <span className="pr-session">Merge order after yesterday</span>
          </span>
        </div>
        <nav className="pr-actions" aria-label="Session">
          <button type="button" className={`pr-btn${drawer === 'log' ? ' is-on' : ''}`} onClick={() => setDrawer(drawer === 'log' ? null : 'log')} aria-expanded={drawer === 'log'}>
            <MessagesSquare size={16} aria-hidden="true" />
            <span>Conversation</span>
          </button>
          <button type="button" className={`pr-btn${drawer === 'board' ? ' is-on' : ''}`} onClick={() => setDrawer(drawer === 'board' ? null : 'board')} aria-expanded={drawer === 'board'}>
            <StickyNote size={16} aria-hidden="true" />
            <span>Board</span>
            <span className="pr-count">{snap.board.length}</span>
          </button>
          <button type="button" className={`pr-btn pr-btn-lab${labOpen ? ' is-on' : ''}`} onClick={() => setLabOpen(!labOpen)} aria-expanded={labOpen} aria-label="Lab controls">
            <SlidersHorizontal size={16} aria-hidden="true" />
          </button>
        </nav>
      </header>

      <section className="pr-subs" aria-label="Last exchange" aria-live="polite" style={{ top: Math.round(focus.y * size.h + focus.size * (subs.long ? 1.05 : 1.12) + 18) }}>
        <p className="pr-state" aria-live="polite" title={look.hint}>
          <i aria-hidden="true" />
          {look.label}
        </p>
        <div className={`pr-subs-inner${subs.you || subs.shiori ? '' : ' is-empty'}`} ref={subsRef}>
          {subs.you && (
            <p className="pr-you">
              <span className="pr-who">You</span>
              {subs.you.text}
            </p>
          )}
          {subs.shiori?.thinking && (
            <p className="pr-reading">
              <span className="pr-who">Shiori</span>
              reading {subs.shiori.reads.length ? subs.shiori.reads.map((r) => r.split('/').pop()).join(', ') : 'the brain'}
              <span className="pr-dots" aria-hidden="true">
                <i />
                <i />
                <i />
              </span>
            </p>
          )}
          {subs.shiori && !subs.shiori.thinking && (
            <p className="pr-answer">
              <Words tokens={subs.shown} streaming={subs.shiori.streaming} />
            </p>
          )}
        </div>
      </section>

      <footer className="pr-dock">
        <div className="pr-modes">
          <div className="pr-seg" role="radiogroup" aria-label="Mode">
            {MODE_ORDER.map((m) => (
              <button key={m} type="button" role="radio" aria-checked={snap.mode === m} className={`pr-seg-btn${snap.mode === m ? ' is-on' : ''}`} style={{ '--c': MODES[m].hex } as React.CSSProperties} onClick={() => switchMode(m)}>
                <span className="pr-seg-power" aria-hidden="true">
                  {Array.from({ length: 3 }, (_, i) => (
                    <i key={i} className={i < MODES[m].power ? 'is-lit' : ''} />
                  ))}
                </span>
                {MODES[m].label}
              </button>
            ))}
          </div>
          <p className="pr-mode-hint">{info.allows}</p>
        </div>
        <div className="pr-composer">
          <button
            type="button"
            className={`pr-mic${talking ? ' is-on' : ''}`}
            aria-label="Hold to talk"
            onPointerDown={() => setTalking(true)}
            onPointerUp={() => setTalking(false)}
            onPointerLeave={() => setTalking(false)}
          >
            <Mic size={18} />
          </button>
          <div className="pr-field">{snap.composer ? <span>{snap.composer}<span className="pr-caret" aria-hidden="true" /></span> : <span className="pr-ph">{info.placeholder}</span>}</div>
          <button type="button" className="pr-send" aria-label="Send" disabled={!snap.composer}>
            <Send size={17} />
          </button>
        </div>
      </footer>

      {drawer && (
        <aside className={`pr-drawer pr-drawer-${drawer}`} aria-label={drawer === 'log' ? 'Conversation' : 'Board'}>
          <div className="pr-drawer-head">
            <h2>{drawer === 'log' ? 'Conversation' : 'Board'}</h2>
            <button type="button" className="pr-icon" onClick={() => setDrawer(null)} aria-label="Close">
              <X size={18} />
            </button>
          </div>
          {drawer === 'log' ? (
            <ol className="pr-log">
              {snap.entries.map((e) =>
                e.kind === 'user' ? (
                  <li key={e.id} className="pr-log-you">
                    <span className="pr-who">You</span>
                    {e.text}
                  </li>
                ) : e.kind === 'shiori' ? (
                  <li key={e.id} className="pr-log-shiori">
                    <span className="pr-who">Shiori</span>
                    {e.thinking ? <span className="pr-muted">reading…</span> : <Words tokens={e.tokens.slice(0, e.shown)} streaming={e.streaming} />}
                  </li>
                ) : (
                  <li key={e.id} className="pr-log-mode" style={{ '--c': MODES[e.mode].hex } as React.CSSProperties}>
                    {MODES[e.mode].label} · {e.by === 'you' ? 'you switched' : 'switched'}
                  </li>
                ),
              )}
            </ol>
          ) : sections.length === 0 ? (
            <p className="pr-muted pr-empty">Nothing on the board yet. Ideas, doubts, decisions and tasks land here as you talk.</p>
          ) : (
            <div className="pr-board">
              {sections.map((s) => (
                <section key={s.kind} className={`pr-board-sec k-${s.kind}`}>
                  <h3>
                    {s.heading} <span className="pr-count">{s.items.length}</span>
                  </h3>
                  <ul>
                    {s.items.map((i) => (
                      <li key={i.id}>
                        {i.text}
                        {i.office && <span className="pr-office">{i.office}</span>}
                      </li>
                    ))}
                  </ul>
                </section>
              ))}
            </div>
          )}
        </aside>
      )}

      {labOpen && (
        <aside className="pr-lab" aria-label="Lab controls">
          <div className="pr-lab-head">
            <span>Lab</span>
            <span className="pr-muted">scripted session, made-up notes</span>
          </div>
          <fieldset>
            <legend>Body</legend>
            <div className="pr-chips">
              {SKINS.map((s) => (
                <button key={s} type="button" className={`pr-chip${prefs.skin === s ? ' is-on' : ''}`} aria-pressed={prefs.skin === s} onClick={() => pick({ skin: s })}>
                  {RIGS[s].label}
                </button>
              ))}
            </div>
            <p className="pr-muted">{RIGS[prefs.skin].line}</p>
          </fieldset>
          <fieldset>
            <legend>Drawing</legend>
            <div className="pr-chips">
              <button type="button" className={`pr-chip${prefs.pixel === 0 ? ' is-on' : ''}`} aria-pressed={prefs.pixel === 0} onClick={() => pick({ pixel: 0 })}>
                Smooth
              </button>
              {[2, 3, 4].map((p) => (
                <button key={p} type="button" className={`pr-chip${prefs.pixel === p ? ' is-on' : ''}`} aria-pressed={prefs.pixel === p} onClick={() => pick({ pixel: p })}>
                  Pixel ×{p}
                </button>
              ))}
            </div>
            <p className="pr-muted">Pixel: Shiori on the world's own grid and palette.</p>
          </fieldset>
          <fieldset>
            <legend>World</legend>
            <div className="pr-chips">
              {WORLD_ORDER.map((id) => (
                <button key={id} type="button" className={`pr-chip${prefs.world === id ? ' is-on' : ''}`} aria-pressed={prefs.world === id} onClick={() => pick({ world: id })}>
                  {WORLDS[id].label}
                </button>
              ))}
            </div>
            <p className="pr-muted">{world.line}</p>
          </fieldset>
          <fieldset>
            <legend>Hour</legend>
            <div className="pr-chips">
              {(['auto', ...HOURS] as const).map((h) => (
                <button key={h} type="button" className={`pr-chip${prefs.hour === h ? ' is-on' : ''}`} aria-pressed={prefs.hour === h} onClick={() => pick({ hour: h })}>
                  {h === 'auto' ? `Now · ${world.hours[clockHour]}` : world.hours[h]}
                </button>
              ))}
            </div>
          </fieldset>
          <fieldset>
            <legend>Weather</legend>
            <div className="pr-chips">
              <button type="button" className={`pr-chip${prefs.sky === 'auto' ? ' is-on' : ''}`} aria-pressed={prefs.sky === 'auto'} onClick={() => pick({ sky: 'auto' })}>
                {realSky ? `Now · ${world.skies[realSky]}` : 'Now'}
              </button>
              {SKIES.map((k) => (
                <button key={k} type="button" className={`pr-chip${prefs.sky === k ? ' is-on' : ''}`} aria-pressed={prefs.sky === k} onClick={() => pick({ sky: k })}>
                  {world.skies[k]}
                </button>
              ))}
            </div>
            <p className="pr-muted">{realSky ? 'Now follows the weather of your town, from Settings.' : 'Now needs a town in Settings › Look and sound; until then it stays clear.'}</p>
          </fieldset>
          <fieldset>
            <legend>State</legend>
            <div className="pr-chips">
              <button type="button" className={`pr-chip${forced === null ? ' is-on' : ''}`} aria-pressed={forced === null} onClick={() => setForced(null)}>
                Auto
              </button>
              {FACE_STATES.map((s) => (
                <button key={s} type="button" className={`pr-chip${forced === s ? ' is-on' : ''}`} aria-pressed={forced === s} onClick={() => setForced(s)}>
                  {FACE_LOOKS[s].label}
                </button>
              ))}
            </div>
          </fieldset>
          <div className="pr-lab-play">
            <button type="button" className="pr-btn" onClick={restart}>
              <RotateCcw size={15} aria-hidden="true" /> Restart
            </button>
            <button type="button" className="pr-btn" onClick={() => setPlaying((p) => !p)} disabled={snap.done}>
              {running ? <Pause size={15} aria-hidden="true" /> : <Play size={15} aria-hidden="true" />} {running ? 'Pause' : 'Play'}
            </button>
            <span className="pr-progress" role="progressbar" aria-label="Session" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100}>
              <span style={{ width: `${progress}%` }} />
            </span>
          </div>
          <label className="pr-check">
            <input type="checkbox" checked={calm} disabled={systemCalm} onChange={(e) => setCalm(e.target.checked)} /> Calm motion
          </label>
        </aside>
      )}
    </div>
  )
}
