import { CircleStop, Ear, History, MessagesSquare, Mic, Palette, Send, StickyNote, X } from 'lucide-react'
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import {
  useCreateShioriSession,
  useEndShioriSession,
  useSendShioriMessage,
  useSetShioriMode,
  useShioriSession,
  useShioriSessions,
  useWeather,
} from '../api/hooks'
import type { ShioriEnding, ShioriMode } from '../api/types'
import { ShioriBody } from '../lab/presence/body/ShioriBody'

import { hourOf, type Hour } from '../lab/presence/world/types'
import { WorldBackdrop } from '../lab/presence/world/WorldBackdrop'
import { WORLDS } from '../lab/presence/world/worlds'
import { FACE_LOOKS } from '../lab/shiori/faceMachine'
import { useReducedMotion } from '../lab/shiori/faces/useFaceLoop'
import { MODE_ORDER, MODES } from '../lab/shiori/modes'
import type { SettingsSection } from '../settings/sections'
import { useDismiss } from '../ui/useDismiss'
import { useToast } from '../ui/toastContext'
import { usePersistentState } from '../usePersistentState'
import { BoardDrawer, ConversationDrawer, SessionsDrawer } from './Drawers'
import { useLive } from './liveStore'
import { loadLook, saveLook, type Look } from './look'
import { LookPanel } from './LookPanel'
import { adviceFor, apiMode, faceFor, liveSubtitles, modeOf, pickSession, stageLayout, titleFrom } from './talk'
import { useEars } from './useEars'
import { useWake } from './useWake'
import { loadByName, saveByName, wakeLabel } from './wake'
import { afterHearing, earsLabel, isTalkKey, type Heard } from './voice'
import { Words } from './Words'
import '../lab/presence/presence.css'
import './shiori-page.css'

/** The selection that means "a new session starts with the next message". */
const NEW = 'new'

type Drawer = 'sessions' | 'log' | 'board' | null

/** Shiori at the centre of her world: the real conversation, saved as sessions in the brain. */
export function ShioriPage({ onOpenSettings }: { onOpenSettings: (section: SettingsSection) => void }) {
  const toast = useToast()
  const sessions = useShioriSessions()
  const [wanted, setWanted] = usePersistentState<string>('openspace.shiori.session', '')
  /** A session just created, before the list has been read again. */
  const [fresh, setFresh] = useState<string>()
  const id = wanted === NEW ? undefined : wanted && wanted === fresh ? wanted : pickSession(sessions.data ?? [], wanted)
  const session = useShioriSession(id)
  const view = session.data
  const live = useLive(id)

  const create = useCreateShioriSession()
  const send = useSendShioriMessage()
  const setMode = useSetShioriMode()
  const end = useEndShioriSession()

  const [text, setText] = useState('')
  const [focused, setFocused] = useState(false)
  const [nextMode, setNextMode] = useState<ShioriMode>('Brainstorm')
  const [problem, setProblem] = useState<string | null>(null)
  const [ending, setEnding] = useState<ShioriEnding | null>(null)
  const [confirmEnd, setConfirmEnd] = useState(false)
  const [drawer, setDrawer] = useState<Drawer>(null)
  const [lookOpen, setLookOpen] = useState(false)
  const [look, setLook] = useState<Look>(loadLook)
  const [size, setSize] = useState({ w: 1000, h: 700, top: 64, dock: 150 })
  const [clockHour, setClockHour] = useState<Hour>(() => hourOf(new Date()))
  const systemCalm = useReducedMotion()
  const calm = systemCalm || look.calm

  const root = useRef<HTMLDivElement>(null)
  const subsRef = useRef<HTMLDivElement>(null)
  const topRef = useRef<HTMLElement>(null)
  const dockRef = useRef<HTMLElement>(null)
  const field = useRef<HTMLTextAreaElement>(null)
  const lookBox = useRef<HTMLElement>(null)
  const lookButton = useRef<HTMLButtonElement>(null)
  const endBox = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const t = setInterval(() => setClockHour(hourOf(new Date())), 60_000)
    return () => clearInterval(t)
  }, [])

  useLayoutEffect(() => {
    const el = root.current!
    const measure = () => setSize({ w: el.clientWidth, h: el.clientHeight, top: topRef.current?.offsetHeight ?? 64, dock: dockRef.current?.offsetHeight ?? 150 })
    const ro = new ResizeObserver(measure)
    if (topRef.current) ro.observe(topRef.current)
    if (dockRef.current) ro.observe(dockRef.current)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const closeLook = useCallback(() => setLookOpen(false), [])
  const closeEnd = useCallback(() => setConfirmEnd(false), [])
  useDismiss(lookOpen, closeLook, lookBox, lookButton)
  useDismiss(confirmEnd, closeEnd, endBox)

  const pick = useCallback((change: Partial<Look>) => {
    setLook((old) => {
      const next = { ...old, ...change }
      saveLook(next)
      return next
    })
  }, [])

  // Her ears: hold the mic (or the space bar) to talk; what is heard is sent like a typed message.
  const submitRef = useRef<(said?: string) => Promise<void>>(async () => {})
  const textRef = useRef(text)
  useEffect(() => {
    textRef.current = text
  })
  const onHeard = (heard: Heard) => {
    const next = afterHearing(heard, textRef.current)
    setText(next.compose)
    if (next.send) void submitRef.current(next.send)
  }
  const voice = useEars(onHeard, (message) => setProblem(message))
  const ended = !!view?.endedAt
  // Or call her by name: the microphone stays open and only "Shiori…" is listened to.
  const [byName, setByName] = useState(loadByName)
  const wake = useWake(byName && !ended, voice.ears === 'recording' || voice.ears === 'asking', onHeard, (message) => setProblem(message))
  // Holding the mic says what the mic does; otherwise always listening tells its state.
  const byNameShown = wake !== 'off' && voice.ears === 'idle'
  const toggleByName = () => {
    setByName(!byName)
    saveByName(!byName)
  }
  const answering = !!view?.answering
  const sending = send.isPending || create.isPending
  const busy = answering || sending
  const mode = view?.mode ?? nextMode
  const info = MODES[modeOf(mode)]
  const subs = liveSubtitles(view?.transcript ?? [], live.draft, answering, live.error)
  const face = faceFor({ typing: (focused && text.trim().length > 0) || voice.ears === 'recording' || wake === 'called', sending: sending || voice.ears === 'hearing' || wake === 'hearing', answering, streaming: answering && live.draft.length > 0, failed: !!subs.failed, ended })
  const faceLook = FACE_LOOKS[face]
  // A failure with its advice needs the room of a long answer, not its card.
  const { focus, subsTop } = stageLayout(size.w, size.h, size.top, size.dock, subs.long || !!subs.failed)
  const realSky = useWeather().data?.now?.sky
  const weather = { hour: look.hour === 'auto' ? clockHour : look.hour, sky: look.sky === 'auto' ? (realSky ?? 'Clear') : look.sky }
  const advice = subs.failed ? adviceFor(subs.failed) : null

  // A long answer reads as a card that follows its last line.
  useEffect(() => {
    const el = subsRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [live.draft, view?.transcript.length])

  // Another session, another ending to tell.
  const [shownId, setShownId] = useState(id)
  if (shownId !== id) {
    setShownId(id)
    setEnding(null)
    setConfirmEnd(false)
    setProblem(null)
  }

  const submit = async (said?: string) => {
    const message = (said ?? text).trim()
    if (!message || busy || ended) return
    setProblem(null)
    try {
      let target = id
      if (!target) {
        const created = await create.mutateAsync({ title: titleFrom(message), mode: nextMode })
        target = created.id
        setFresh(created.id)
        setWanted(created.id)
      }
      if (said === undefined) setText('')
      await send.mutateAsync({ sessionId: target, text: message })
      void session.refetch()
    } catch (e) {
      setText(message)
      setProblem(e instanceof Error ? e.message : String(e))
    }
  }
  useEffect(() => {
    submitRef.current = submit
  })

  useEffect(() => {
    if (ended) return
    const down = (e: KeyboardEvent) => {
      if (!isTalkKey(e, e.isComposing)) return
      e.preventDefault()
      void voice.start()
    }
    const up = (e: KeyboardEvent) => {
      if (e.code === 'Space') voice.stop()
      if (e.code === 'Escape') voice.cancel()
    }
    window.addEventListener('keydown', down)
    window.addEventListener('keyup', up)
    return () => {
      window.removeEventListener('keydown', down)
      window.removeEventListener('keyup', up)
    }
  }, [ended, voice])

  const switchMode = (m: ShioriMode) => {
    if (m === mode) return
    if (!view) {
      setNextMode(m)
      return
    }
    setProblem(null)
    setMode.mutate({ sessionId: view.id, mode: m }, { onError: (e) => setProblem(e.message) })
  }

  const finish = () => {
    if (!view) return
    setConfirmEnd(false)
    end.mutate(view.id, {
      onSuccess: (result) => {
        setEnding(result)
        toast(`Session saved as ${result.session.note}`)
      },
      onError: (e) => setProblem(e.message),
    })
  }

  const startNew = () => {
    setWanted(NEW)
    setDrawer(null)
    setText('')
    field.current?.focus()
  }

  const title = view?.title ?? (wanted === NEW || !sessions.data?.length ? 'New session' : sessions.isPending ? 'Opening…' : 'New session')
  const unreachable = sessions.error && !sessions.data

  return (
    <div
      ref={root}
      className={`pr sp${subs.long ? ' is-long' : ''}${calm ? ' is-calm' : ''}`}
      data-mode={modeOf(mode)}
      style={{ '--mode': info.hex, '--sp-top': `${size.top}px` } as React.CSSProperties}
    >
      <WorldBackdrop world={look.world} pixel={look.pixel} calm={calm} weather={weather} />
      <div className="pr-shade" aria-hidden="true" />
      <ShioriBody state={face} skin={look.skin} mode={modeOf(mode)} pixel={look.pixel} palette={WORLDS[look.world].palette} calm={calm} focus={focus} notes={subs.notes} />

      <header className="pr-top" ref={topRef}>
        <div className="pr-brand">
          <span className="pr-mark" aria-hidden="true">
            栞
          </span>
          <span className="pr-brand-text">
            <span className="pr-name">SHIORI</span>
            <span className="pr-session sp-title" title={title}>
              {title}
              {ended && <span className="sp-tag">Ended</span>}
            </span>
          </span>
        </div>
        <nav className="pr-actions" aria-label="Session">
          <button type="button" className={`pr-btn${drawer === 'sessions' ? ' is-on' : ''}`} onClick={() => setDrawer(drawer === 'sessions' ? null : 'sessions')} aria-expanded={drawer === 'sessions'} aria-label="Sessions">
            <History size={16} aria-hidden="true" />
            <span>Sessions</span>
          </button>
          <button type="button" className={`pr-btn${drawer === 'log' ? ' is-on' : ''}`} onClick={() => setDrawer(drawer === 'log' ? null : 'log')} aria-expanded={drawer === 'log'} aria-label="Conversation">
            <MessagesSquare size={16} aria-hidden="true" />
            <span>Conversation</span>
          </button>
          <button type="button" className={`pr-btn${drawer === 'board' ? ' is-on' : ''}`} onClick={() => setDrawer(drawer === 'board' ? null : 'board')} aria-expanded={drawer === 'board'} aria-label="Board">
            <StickyNote size={16} aria-hidden="true" />
            <span>Board</span>
            <span className="pr-count">{view?.board.length ?? 0}</span>
          </button>
          <button ref={lookButton} type="button" className={`pr-btn${lookOpen ? ' is-on' : ''}`} onClick={() => setLookOpen(!lookOpen)} aria-expanded={lookOpen} aria-label="Look">
            <Palette size={16} aria-hidden="true" />
          </button>
          {view && !ended && (
            <div className="sp-end" ref={endBox}>
              <button type="button" className={`pr-btn${confirmEnd ? ' is-on' : ''}`} onClick={() => setConfirmEnd(!confirmEnd)} aria-expanded={confirmEnd} aria-label="End session" disabled={end.isPending}>
                <CircleStop size={16} aria-hidden="true" />
                <span>{end.isPending ? 'Ending…' : 'End'}</span>
              </button>
              {confirmEnd && (
                <div className="sp-confirm" role="dialog" aria-label="End this session">
                  <p>
                    End this session? Its note is written to <code>{view.note}</code> and the board's decisions become proposals for you to approve.
                    {answering && ' The answer being written stops.'}
                  </p>
                  <div className="sp-confirm-row">
                    <button type="button" className="pr-btn" onClick={() => setConfirmEnd(false)}>
                      Keep talking
                    </button>
                    <button type="button" className="pr-btn is-on" onClick={finish}>
                      End session
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}
        </nav>
      </header>

      <section className="pr-subs" aria-label="Last exchange" style={{ top: subsTop, bottom: size.dock + 8 }}>
        <p className="pr-state" aria-live="polite" title={faceLook.hint}>
          <i aria-hidden="true" />
          {faceLook.label}
        </p>
        {unreachable ? (
          <div className="pr-subs-inner sp-notice" role="alert">
            <p className="sp-failed-text">Shiori's sessions are unreachable: {sessions.error.message}. Check that the Openspace server is running, then reload.</p>
          </div>
        ) : !view && !id ? (
          <div className="pr-subs-inner sp-notice">
            <p className="pr-answer sp-hello">Write to Shiori below: a new session starts and is named after what you say.</p>
          </div>
        ) : (
          <div className={`pr-subs-inner${subs.you || subs.answer || subs.failed ? '' : ' is-empty'}`} ref={subsRef} aria-live="polite">
            {subs.you && (
              <p className="pr-you sp-pre">
                <span className="pr-who">You</span>
                {subs.you}
              </p>
            )}
            {subs.thinking && (
              <p className="pr-reading">
                <span className="pr-who">Shiori</span>
                reading the brain
                <span className="pr-dots" aria-hidden="true">
                  <i />
                  <i />
                  <i />
                </span>
              </p>
            )}
            {subs.answer && (
              <p className="pr-answer">
                <Words tokens={subs.answer.tokens} streaming={subs.answer.streaming} />
              </p>
            )}
            {subs.failed && (
              <div className="sp-failed" role="alert">
                <span className="pr-who">Shiori could not answer</span>
                <p className="sp-failed-text">{subs.failed}</p>
                {advice && (
                  <button type="button" className="pr-btn" onClick={() => onOpenSettings(advice.settings)}>
                    {advice.label}
                  </button>
                )}
              </div>
            )}
          </div>
        )}
      </section>

      <footer className="pr-dock" ref={dockRef}>
        <div className="pr-modes">
          <div className="pr-seg" role="radiogroup" aria-label="Mode">
            {MODE_ORDER.map((m) => (
              <button
                key={m}
                type="button"
                role="radio"
                aria-checked={modeOf(mode) === m}
                className={`pr-seg-btn${modeOf(mode) === m ? ' is-on' : ''}`}
                style={{ '--c': MODES[m].hex } as React.CSSProperties}
                onClick={() => switchMode(apiMode(m))}
                disabled={ended || setMode.isPending}
              >
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
        {problem && (
          <p className="sp-problem" role="alert">
            <span>{problem}</span>
            <button type="button" className="pr-icon sp-problem-close" onClick={() => setProblem(null)} aria-label="Dismiss">
              <X size={15} />
            </button>
          </p>
        )}
        {ended && view ? (
          <div className="pr-composer sp-ended">
            <div className="sp-ended-text">
              <span>This session has ended and is read only.</span>
              <span className="pr-muted">
                Its note: <code>{view.note}</code>
                {ending && ending.proposed.length > 0 && (
                  <>
                    {' '}
                    · proposed: {ending.proposed.map((p) => <code key={p}>{p}</code>)}
                  </>
                )}
              </span>
            </div>
            <button type="button" className="pr-btn is-on" onClick={startNew}>
              New session
            </button>
          </div>
        ) : (
          <form
            className="pr-composer"
            onSubmit={(e) => {
              e.preventDefault()
              void submit()
            }}
          >
            <button
              type="button"
              className={`pr-mic${voice.ears === 'recording' ? ' is-on' : ''}`}
              aria-label="Hold to talk to Shiori"
              title={earsLabel(voice.ears, voice.ms)}
              disabled={voice.ears === 'unavailable' || voice.ears === 'hearing'}
              style={{ '--level': voice.level } as React.CSSProperties}
              onPointerDown={(e) => {
                e.currentTarget.setPointerCapture(e.pointerId)
                void voice.start()
              }}
              onPointerUp={() => voice.stop()}
              onPointerCancel={() => voice.cancel()}
              onKeyDown={(e) => {
                if ((e.key === 'Enter' || e.key === ' ') && !e.repeat) {
                  e.preventDefault()
                  void voice.start()
                }
              }}
              onKeyUp={(e) => {
                if (e.key === 'Enter' || e.key === ' ') voice.stop()
              }}
            >
              <Mic size={18} />
            </button>
            <button
              type="button"
              className={`pr-mic${byName ? ' is-on' : ''}`}
              aria-pressed={byName}
              aria-label="Listen for her name"
              title={byName ? 'Listening for “Shiori…”: click to stop' : 'Listen for her name: say “Shiori…” instead of holding the mic'}
              onClick={toggleByName}
            >
              <Ear size={18} />
            </button>
            <textarea
              ref={field}
              className="pr-field sp-field"
              rows={1}
              value={text}
              placeholder={busy ? 'Shiori is answering: you can write the next one meanwhile' : info.placeholder}
              aria-label="Message to Shiori"
              onChange={(e) => setText(e.target.value)}
              onFocus={() => setFocused(true)}
              onBlur={() => setFocused(false)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
                  e.preventDefault()
                  void submit()
                }
              }}
            />
            <button type="submit" className="pr-send" aria-label="Send" disabled={!text.trim() || busy}>
              <Send size={17} />
            </button>
          </form>
        )}
        {!ended && (
          <p className={`sp-ears is-${byNameShown ? wake : voice.ears}`} aria-live="polite">
            {byNameShown ? wakeLabel(wake) : earsLabel(voice.ears, voice.ms)}
          </p>
        )}
      </footer>

      {drawer === 'sessions' && (
        <SessionsDrawer
          sessions={sessions.data}
          loading={sessions.isPending}
          current={id}
          onPick={(s) => {
            setWanted(s)
            setDrawer(null)
          }}
          onNew={startNew}
          onClose={() => setDrawer(null)}
        />
      )}
      {drawer === 'log' && <ConversationDrawer view={view} draft={live.draft} onClose={() => setDrawer(null)} />}
      {drawer === 'board' && <BoardDrawer view={view} onClose={() => setDrawer(null)} />}
      {lookOpen && <LookPanel ref={lookBox} look={look} onPick={pick} onClose={closeLook} clockHour={clockHour} realSky={realSky} systemCalm={systemCalm} />}
    </div>
  )
}
