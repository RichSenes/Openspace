import { Feather, Grid3x3, Mic, Orbit, Pause, Play, RotateCcw, Send, StickyNote, type LucideIcon } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { FACE_LOOKS, FACE_STATES, type FaceState } from './faceMachine'
import { HoloOrb } from './faces/HoloOrb'
import { InkWeb } from './faces/InkWeb'
import { PixelEyes } from './faces/PixelEyes'
import { useReducedMotion, type FaceProps } from './faces/useFaceLoop'
import { ModeSwitch } from './ModeSwitch'
import { MODES, type Mode } from './modes'
import { buildTimeline, SESSION, snapshot, timeOf, type ManualSwitch } from './script'
import { Cite, Transcript } from './Transcript'
import { Whiteboard, type BoardPhase } from './Whiteboard'
import './shiori.css'

type Direction = 'a' | 'b' | 'c'

const DIRECTIONS: { id: Direction; letter: string; name: string; short: string; line: string; icon: LucideIcon }[] = [
  { id: 'a', letter: 'A', name: 'Living diorama', short: 'Diorama', line: 'Pixel all the way', icon: Grid3x3 },
  { id: 'b', letter: 'B', name: 'Holo lab', short: 'Holo lab', line: 'Pixel world, light for the mind', icon: Orbit },
  { id: 'c', letter: 'C', name: 'Storybook atlas', short: 'Storybook', line: 'Ink and paper over pixels', icon: Feather },
]

const STORE = 'openspace.lab.shiori.direction'

function loadDirection(): Direction {
  try {
    const v = localStorage.getItem(STORE)
    return v === 'a' || v === 'b' || v === 'c' ? v : 'a'
  } catch {
    return 'a'
  }
}

function saveDirection(d: Direction) {
  try {
    localStorage.setItem(STORE, d)
  } catch {
    // Private windows may refuse storage: the switch still works for this visit.
  }
}

function useNarrow(px: number) {
  const q = `(max-width: ${px}px)`
  const [on, setOn] = useState(() => matchMedia(q).matches)
  useEffect(() => {
    const mq = matchMedia(q)
    const f = () => setOn(mq.matches)
    mq.addEventListener('change', f)
    return () => mq.removeEventListener('change', f)
  }, [q])
  return on
}

function Face({ direction, ...props }: FaceProps & { direction: Direction }) {
  if (direction === 'a') return <PixelEyes {...props} />
  if (direction === 'b') return <HoloOrb {...props} />
  return <InkWeb {...props} />
}

const TICK = 50

/** `?lab=shiori`: Shiori's chat, face and whiteboard in the three candidate directions. */
export function ShioriLab() {
  const timeline = useMemo(() => buildTimeline(SESSION), [])
  const [direction, setDirection] = useState<Direction>(loadDirection)
  const [t, setT] = useState(0)
  const [playing, setPlaying] = useState(true)
  const [manual, setManual] = useState<ManualSwitch[]>([])
  const [forced, setForced] = useState<FaceState | null>(null)
  const [talking, setTalking] = useState(false)
  const [calm, setCalm] = useState(false)
  const [phase, setPhase] = useState<BoardPhase>('board')
  const [boardOpen, setBoardOpen] = useState(false)
  const [seen, setSeen] = useState(0)
  const systemCalm = useReducedMotion()
  const reduced = systemCalm || calm
  const narrow = useNarrow(760)
  const tRef = useRef(t)
  useEffect(() => {
    tRef.current = t
  }, [t])

  const snap = useMemo(() => snapshot(timeline, t, manual), [timeline, t, manual])
  const face: FaceState = talking ? 'listening' : (forced ?? snap.face)

  const running = playing && !snap.done
  useEffect(() => {
    if (!running) return
    const id = setInterval(() => setT((x) => Math.min(timeline.duration, x + TICK)), TICK)
    return () => clearInterval(id)
  }, [running, timeline.duration])

  const pick = useCallback((d: Direction) => {
    setDirection(d)
    saveDirection(d)
  }, [])

  const switchMode = useCallback(
    (m: Mode) => setManual((list) => (m === snapshot(timeline, tRef.current, list).mode ? list : [...list, { t: tRef.current, mode: m }])),
    [timeline],
  )

  const restart = useCallback(() => {
    setT(0)
    setManual([])
    setPhase('board')
    setSeen(0)
    setPlaying(true)
  }, [])

  const fold = useCallback(() => {
    setPhase('folding')
    setTimeout(() => setPhase('note'), reduced ? 0 : 1150)
  }, [reduced])

  // The board's toggle on phones says when something new was written while it was closed.
  const unseen = boardOpen ? 0 : Math.max(0, snap.board.length - seen)
  const toggleBoard = (open: boolean) => {
    setSeen(snap.board.length)
    setBoardOpen(open)
  }

  // A small handle for screenshots and quick checks from the console.
  useEffect(() => {
    const api = {
      direction: (d: Direction) => pick(d),
      state: (s: FaceState | null) => setForced(s),
      seek: (step: number | 'end', at: 'start' | 'mid' | 'end' = 'mid') => {
        setPlaying(false)
        setPhase('board')
        setT(step === 'end' ? timeline.duration : timeOf(timeline, step, at))
      },
      play: () => setPlaying(true),
      pause: () => setPlaying(false),
      fold: () => fold(),
      board: (open: boolean) => setBoardOpen(open),
      calm: (on: boolean) => setCalm(on),
    }
    ;(window as unknown as { __shioriLab: typeof api }).__shioriLab = api
  }, [pick, timeline, fold])

  const look = FACE_LOOKS[face]
  const info = MODES[snap.mode]
  const progress = Math.round((t / timeline.duration) * 100)

  const board = (
    <Whiteboard
      items={snap.board}
      done={snap.done}
      links={snap.links}
      phase={phase}
      onFold={fold}
      onUnfold={() => setPhase('board')}
      onClose={narrow ? () => toggleBoard(false) : undefined}
    />
  )

  return (
    <div className={`sh dir-${direction}${reduced ? ' is-calm' : ''}`} data-mode={snap.mode} style={{ '--mode': info.color, '--face': look.color } as React.CSSProperties}>
      <header className="sh-bar">
        <div className="sh-brand">
          <span className="sh-brand-name">Shiori lab</span>
          <span className="hint">Prototype · scripted session, made-up notes</span>
        </div>
        <div className="sh-dirs" role="radiogroup" aria-label="Visual direction">
          {DIRECTIONS.map((d) => {
            const Icon = d.icon
            return (
              <button key={d.id} type="button" role="radio" aria-checked={direction === d.id} className={`sh-dir${direction === d.id ? ' is-on' : ''}`} onClick={() => pick(d.id)}>
                <Icon size={15} aria-hidden="true" />
                <span className="sh-dir-letter">{d.letter}</span>
                <span className="sh-dir-text">
                  <span className="sh-dir-name">{d.name}</span>
                  <span className="sh-dir-short">{d.short}</span>
                  <span className="sh-dir-line">{d.line}</span>
                </span>
              </button>
            )
          })}
        </div>
        <div className="sh-play">
          <button type="button" className="btn btn-quiet" onClick={restart} aria-label="Restart the session">
            <RotateCcw size={15} /> <span className="sh-play-word">Restart</span>
          </button>
          <button type="button" className="btn btn-quiet" onClick={() => setPlaying((p) => !p)} disabled={snap.done} aria-label={running ? 'Pause' : 'Play'}>
            {running ? <Pause size={15} /> : <Play size={15} />} <span className="sh-play-word">{running ? 'Pause' : 'Play'}</span>
          </button>
          <span className="sh-progress" role="progressbar" aria-label="Session" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100}>
            <span style={{ width: `${progress}%` }} />
          </span>
        </div>
      </header>

      <main className="sh-stage">
        <aside className="sh-face-panel" aria-label="Shiori">
          <div className="sh-face" key={direction}>
            <Face direction={direction} state={face} modeHex={info.hex} reduced={reduced} compact={narrow} />
          </div>
          <div className="sh-face-caption">
            <span className="sh-name">Shiori</span>
            <span className="sh-state" aria-live="polite">
              <i aria-hidden="true" />
              {look.label}
              {forced && <span className="sh-forced">forced</span>}
            </span>
          </div>
          <div className="sh-face-about">
            <p className="sh-hint">{look.hint}</p>
            <h2 className="sh-about-title">Cited in this session</h2>
            {snap.links.length === 0 ? (
              <p className="hint">Nothing yet. Every answer names the notes it comes from.</p>
            ) : (
              <ul className="sh-cited">
                {snap.links.map((l) => (
                  <li key={l}>
                    <Cite path={l} />
                  </li>
                ))}
              </ul>
            )}
          </div>
          {narrow && (
            <button type="button" className={`btn sh-board-toggle${unseen ? ' has-new' : ''}`} onClick={() => toggleBoard(!boardOpen)} aria-expanded={boardOpen}>
              <StickyNote size={15} aria-hidden="true" /> Board
              <span className="sh-board-n">{snap.board.length}</span>
              {unseen > 0 && <span className="sh-board-new" aria-label={`${unseen} new`} />}
            </button>
          )}
          <div className="sh-debug" role="group" aria-label="Force a face state">
            <span className="sh-debug-label">Face</span>
            <button type="button" className={`chip${forced === null ? ' is-on' : ''}`} onClick={() => setForced(null)}>
              Auto
            </button>
            {FACE_STATES.map((s) => (
              <button key={s} type="button" className={`chip${forced === s ? ' is-on' : ''}`} onClick={() => setForced(s)}>
                {s}
              </button>
            ))}
            <label className="chip chip-check">
              <input type="checkbox" checked={reduced} disabled={systemCalm} onChange={(e) => setCalm(e.target.checked)} /> calm motion
            </label>
          </div>
        </aside>

        <section className="sh-chat" aria-label="Chat">
          <div className="sh-chat-head">
            <h1>Merge order after yesterday</h1>
            <span className="hint">Sessions/2026-11-21 · {snap.links.length} {snap.links.length === 1 ? 'note' : 'notes'} cited</span>
          </div>
          <Transcript entries={snap.entries} mode={snap.mode} onSwitch={switchMode} />
          <div className="sh-dock">
            <ModeSwitch mode={snap.mode} onSwitch={switchMode} />
            <div className="composer">
              <button
                type="button"
                className={`btn btn-quiet composer-mic${talking ? ' is-on' : ''}`}
                aria-label="Hold to talk"
                title="Hold to talk"
                onPointerDown={() => setTalking(true)}
                onPointerUp={() => setTalking(false)}
                onPointerLeave={() => setTalking(false)}
              >
                <Mic size={17} />
                {talking && (
                  <span className="meter" aria-hidden="true">
                    <i />
                    <i />
                    <i />
                  </span>
                )}
              </button>
              <div className={`composer-field${snap.composer ? ' has-text' : ''}`}>
                {snap.composer ? (
                  <span>
                    {snap.composer}
                    <span className="caret" aria-hidden="true" />
                  </span>
                ) : (
                  <span className="composer-ph">{info.placeholder}</span>
                )}
              </div>
              <button type="button" className="btn composer-send" aria-label="Send" disabled={!snap.composer}>
                <Send size={16} />
              </button>
            </div>
          </div>
        </section>

        {narrow ? boardOpen && <div className="sh-sheet">{board}</div> : <aside className="sh-board">{board}</aside>}
      </main>
    </div>
  )
}
