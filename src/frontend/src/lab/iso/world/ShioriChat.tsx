import { ArrowUpRight, ChevronsDown, ChevronsUp, Ear, Mic, Send, X } from 'lucide-react'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useCreateShioriSession, useSendShioriMessage, useSetShioriMode, useShioriSession, useShioriSessions } from '../../../api/hooks'
import type { ShioriMode, WorldView } from '../../../api/types'
import { ShioriBody } from '../../presence/body/ShioriBody'
import { RIGS } from '../../presence/body/rig'
import { WORLDS } from '../../presence/world/worlds'
import { useReducedMotion } from '../../shiori/faces/useFaceLoop'
import { tokenize } from '../../shiori/script'
import { MODE_ORDER, MODES } from '../../shiori/modes'
import { SessionList } from '../../../shiori/Drawers'
import { useLive } from '../../../shiori/liveStore'
import { loadLook, PIXELS, saveLook, SKINS, type Look } from '../../../shiori/look'
import { nextSentences } from '../../../shiori/speech'
import { answerText, apiMode, faceFor, modeOf, pickSession, titleFrom } from '../../../shiori/talk'
import { useEars } from '../../../shiori/useEars'
import { useVoice } from '../../../shiori/useVoice'
import { useWake } from '../../../shiori/useWake'
import { afterHearing, earsLabel, type Heard } from '../../../shiori/voice'
import { wakeLabel } from '../../../shiori/wake'
import { Words } from '../../../shiori/Words'
import { usePersistentState } from '../../../usePersistentState'
import { Queue } from './Cards'
import { openPage } from './pages'
import '../../presence/presence.css'
import '../../../shiori/shiori-page.css'

/**
 * Shiori in the lab, compact: a small card of her in the top corner, her face moving as she
 * listens, thinks and answers; under it a plain chat, a box to write or hold to talk, the modes
 * and listening for her name; tabs for her sessions, her look and who waits for you. Folded, only
 * her card, her last words and the box stay: she keeps listening while the lab is in view.
 */

const NEW = 'new'
const TABS = ['chat', 'sessions', 'look', 'waiting'] as const
type Tab = (typeof TABS)[number]

interface Props {
  world: WorldView | undefined
  pending: Heard | null
  onPendingTaken: () => void
  onClose: () => void
  byName: boolean
  onByName: (on: boolean) => void
  folded: boolean
  onFold: (folded: boolean) => void
  /** A page is open over the lab: she stops listening for her name until it closes. */
  paused?: boolean
}

export function ShioriChat({ world, pending, onPendingTaken, onClose, byName, onByName, folded, onFold, paused = false }: Props) {
  const [tab, setTab] = useState<Tab>('chat')
  const [from, setFrom] = useState<'left' | 'right'>('right')
  const sessions = useShioriSessions()
  const [wanted, setWanted] = usePersistentState<string>('openspace.shiori.session', '')
  const [fresh, setFresh] = useState<string>()
  const id = wanted === NEW ? undefined : wanted && wanted === fresh ? wanted : pickSession(sessions.data ?? [], wanted)
  const session = useShioriSession(id)
  const view = session.data
  const live = useLive(id)
  const create = useCreateShioriSession()
  const send = useSendShioriMessage()
  const setMode = useSetShioriMode()
  const [text, setText] = useState('')
  const [nextMode, setNextMode] = useState<ShioriMode>('Brainstorm')
  const [problem, setProblem] = useState<string | null>(null)
  const [look, setLook] = useState<Look>(loadLook)
  const systemCalm = useReducedMotion()
  const log = useRef<HTMLOListElement>(null)

  const answering = !!view?.answering
  const busy = answering || send.isPending || create.isPending
  const ended = !!view?.endedAt
  const mode = view?.mode ?? nextMode
  const entries = view?.transcript ?? []
  const latest = answerText(entries, live.draft, answering)

  const go = (t: Tab) => {
    if (folded) onFold(false)
    if (t === tab) return
    setFrom(TABS.indexOf(t) > TABS.indexOf(tab) ? 'right' : 'left')
    setTab(t)
  }
  const pick = (change: Partial<Look>) => {
    const next = { ...look, ...change }
    saveLook(next)
    setLook(next)
  }

  // Her voice: what was spoken gets a spoken answer, a sentence at a time as she writes it.
  const mouth = useVoice((m) => setProblem(m))
  const heardIn = useRef('it')
  const [spokenAfter, setSpokenAfter] = useState<string | null | undefined>(undefined)
  const spokenTurn = spokenAfter !== undefined && latest.turn !== spokenAfter
  const spoken = useRef({ turn: latest.turn, from: 0 })
  const { say } = mouth
  useEffect(() => {
    if (spoken.current.turn !== latest.turn) spoken.current = { turn: latest.turn, from: 0 }
    if (!spokenTurn || !latest.text) return
    const { parts, next } = nextSentences(latest.text, spoken.current.from, !answering)
    spoken.current.from = next
    void say(parts, heardIn.current)
  }, [latest.text, latest.turn, answering, say, spokenTurn])

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
      setSpokenAfter(said !== undefined ? latest.turn : undefined)
      mouth.stop()
      mouth.begin()
      await send.mutateAsync({ sessionId: target, text: message, spoken: said !== undefined })
      void session.refetch()
    } catch (e) {
      setText(message)
      setProblem(e instanceof Error ? e.message : String(e))
    }
  }
  const submitRef = useRef(submit)
  const textRef = useRef(text)
  const busyRef = useRef(busy)
  useEffect(() => {
    submitRef.current = submit
    textRef.current = text
    busyRef.current = busy
  })
  // Said while she is still answering: kept, and sent together once she is done.
  const heardMeanwhile = useRef<string[]>([])
  const onHeard = (heard: Heard) => {
    if (heard.language) heardIn.current = heard.language
    if (busyRef.current && !textRef.current.trim()) {
      if (heard.text.trim()) heardMeanwhile.current.push(heard.text.trim())
      return
    }
    const next = afterHearing(heard, textRef.current)
    setText(next.compose)
    if (next.send) void submitRef.current(next.send)
  }
  useEffect(() => {
    if (busy || !heardMeanwhile.current.length) return
    const said = heardMeanwhile.current.join(' ')
    heardMeanwhile.current = []
    void submitRef.current(said)
  }, [busy])
  const ears = useEars(onHeard, (m) => setProblem(m))
  // Listening for her name, as on her page: "Shiori…" starts talking, a goodbye ends it.
  const { wake, end: endTalk } = useWake(byName && !ended && !paused, ears.ears === 'recording' || ears.ears === 'asking' || mouth.speaking, onHeard, (m) => setProblem(m))
  const byNameShown = wake !== 'off' && ears.ears === 'idle'
  const { warm } = mouth
  useEffect(() => {
    if (wake === 'talking') void warm()
  }, [wake, warm])

  // Said to her by name in the lab before this opened: sent at once.
  useEffect(() => {
    if (!pending || busy || sessions.isPending) return
    onPendingTaken()
    if (pending.language) heardIn.current = pending.language
    void submitRef.current(pending.text)
  }, [pending, busy, sessions.isPending, onPendingTaken])

  // The chat follows its last line.
  useEffect(() => {
    log.current?.lastElementChild?.scrollIntoView?.({ block: 'end' })
  }, [entries.length, live.draft, tab, folded])

  // The line under the active tab slides to it.
  const tabsRef = useRef<HTMLDivElement>(null)
  const [ink, setInk] = useState({ x: 0, w: 0 })
  const waiting = world?.waiting.length ?? 0
  useLayoutEffect(() => {
    const on = tabsRef.current?.querySelector<HTMLElement>(`[data-tab="${tab}"]`)
    if (on) setInk({ x: on.offsetLeft, w: on.offsetWidth })
  }, [tab, waiting, folded])

  // Folded, the panel is as tall as what stays: the top, her last words and the box.
  const root = useRef<HTMLDivElement>(null)
  const head = useRef<HTMLElement>(null)
  const last = useRef<HTMLParagraphElement>(null)
  const dock = useRef<HTMLElement>(null)
  useLayoutEffect(() => {
    const panel = root.current?.parentElement
    if (!panel) return
    const measure = () => {
      const h = (head.current?.offsetHeight ?? 0) + (last.current?.offsetHeight ?? 0) + (dock.current?.offsetHeight ?? 0)
      panel.style.setProperty('--sc-folded', `${h + 2}px`)
    }
    measure()
    const watch = new ResizeObserver(measure)
    for (const el of [head.current, last.current, dock.current]) if (el) watch.observe(el)
    return () => watch.disconnect()
  }, [folded, tab])

  const face = faceFor({
    typing: text.trim().length > 0 || ears.ears === 'recording',
    sending: send.isPending || create.isPending || ears.ears === 'hearing',
    answering,
    streaming: answering && live.draft.length > 0,
    failed: false,
    ended,
  })
  const switchMode = (m: ShioriMode) => {
    if (m === mode) return
    if (!view) return setNextMode(m)
    setMode.mutate({ sessionId: view.id, mode: m }, { onError: (e) => setProblem(e.message) })
  }
  const title = view?.title ?? 'New conversation'
  const showDock = folded || tab === 'chat'
  const lastWords = answering ? live.draft : (entries.findLast((e) => e.role === 'Shiori')?.text ?? '')

  return (
    <div ref={root} className={`sc${folded ? ' is-folded' : ''}`} data-mode={modeOf(mode)} style={{ '--mode': MODES[modeOf(mode)].hex } as React.CSSProperties}>
      <header className="sc-head" ref={head}>
        <div className="sc-tabs" role="tablist" aria-label="Shiori" ref={tabsRef}>
          {TABS.map((t) => (
            <button key={t} type="button" role="tab" data-tab={t} aria-selected={!folded && tab === t} onClick={() => go(t)}>
              {t === 'chat' ? 'Chat' : t === 'sessions' ? 'Sessions' : t === 'look' ? 'Look' : `Waiting${waiting ? ` (${waiting})` : ''}`}
            </button>
          ))}
          <span className="sc-ink" aria-hidden="true" style={{ transform: `translateX(${ink.x}px)`, width: ink.w }} />
          <button
            type="button"
            className="sc-icon sc-fold"
            onClick={() => onFold(!folded)}
            aria-label={folded ? 'Show the chat' : 'Fold: keep only her and the box'}
            title={folded ? 'Show the chat' : 'Fold: keep only her and the box'}
          >
            {folded ? <ChevronsDown size={16} aria-hidden="true" /> : <ChevronsUp size={16} aria-hidden="true" />}
          </button>
          <button type="button" className="sc-icon" onClick={onClose} aria-label="Close" title="Close">
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <div className="sc-who">
          <div className="sc-name">
            <span className="sc-mark" aria-hidden="true">
              栞
            </span>
            <div>
              <p className="sc-title">Shiori</p>
              <p className="sc-session" title={title}>
                {title}
              </p>
            </div>
          </div>
          {/* Her, small: a card in the corner, her face following the conversation. The portrait
              needs every pixel of the card to read as a face: one art pixel per screen pixel, closer. */}
          <div className={`sc-face is-${look.skin}`} aria-hidden="true">
            <ShioriBody
              state={face}
              skin={look.skin}
              mode={modeOf(mode)}
              pixel={look.skin === 'portrait' && look.pixel ? 1 : look.pixel}
              palette={WORLDS[look.world].palette}
              calm={look.calm || systemCalm}
              focus={look.skin === 'portrait' ? { x: 0.5, y: 0.7, size: 60 } : { x: 0.5, y: 0.5, size: 28 }}
              notes={[]}
            />
          </div>
        </div>
      </header>

      {folded ? (
        <p className="sc-last" ref={last} aria-live="polite">
          {lastWords ? <Words tokens={tokenize(lastWords)} streaming={answering} /> : answering ? 'thinking…' : 'Folded: she still hears you. Write, hold the mic or call her by name.'}
        </p>
      ) : (
        <div key={tab} className={`sc-body from-${from}`} role="tabpanel" aria-label={tab}>
          {tab === 'chat' && (
            <ol className="sc-log" ref={log} aria-live="polite">
              {entries.length === 0 && !answering && <li className="sc-empty">Ask her anything, or hold the microphone to talk.</li>}
              {entries.map((e, i) =>
                e.role === 'Person' ? (
                  <li key={i} className="sc-you">
                    {e.text}
                  </li>
                ) : e.role === 'Shiori' ? (
                  <li key={i} className="sc-her">
                    <Words tokens={tokenize(e.text)} streaming={false} />
                  </li>
                ) : e.role === 'Failed' ? (
                  <li key={i} className="sc-failed">
                    She could not answer: {e.text}
                  </li>
                ) : (
                  <li key={i} className="sc-mark-line">
                    {e.role === 'Ended' ? 'Session ended' : `${MODES[modeOf(e.mode)].label} mode`}
                  </li>
                ),
              )}
              {answering && <li className="sc-her">{live.draft ? <Words tokens={tokenize(live.draft)} streaming /> : <span className="sc-thinking">thinking…</span>}</li>}
            </ol>
          )}

          {tab === 'sessions' && (
            <div className="sc-pane sc-sessions">
              <SessionList
                sessions={sessions.data}
                loading={sessions.isPending}
                current={id}
                onPick={(s) => {
                  setWanted(s)
                  go('chat')
                }}
                onNew={() => {
                  setWanted(NEW)
                  go('chat')
                }}
              />
            </div>
          )}

          {tab === 'look' && (
            <div className="sc-pane sc-look">
              <fieldset>
                <legend>Body</legend>
                <div className="pr-chips">
                  {SKINS.map((s) => (
                    <button key={s} type="button" className={`pr-chip${look.skin === s ? ' is-on' : ''}`} aria-pressed={look.skin === s} onClick={() => pick({ skin: s })}>
                      {RIGS[s].label}
                    </button>
                  ))}
                </div>
                <p className="pr-muted">{RIGS[look.skin].line}</p>
              </fieldset>
              <fieldset>
                <legend>Drawing</legend>
                <div className="pr-chips">
                  {PIXELS.map((p) => (
                    <button key={p} type="button" className={`pr-chip${look.pixel === p ? ' is-on' : ''}`} aria-pressed={look.pixel === p} onClick={() => pick({ pixel: p })}>
                      {p === 0 ? 'Smooth' : `Pixel ×${p}`}
                    </button>
                  ))}
                </div>
              </fieldset>
              <label className="pr-check">
                <input type="checkbox" checked={look.calm || systemCalm} disabled={systemCalm} onChange={(e) => pick({ calm: e.target.checked })} /> Calm motion
                {systemCalm && <span className="pr-muted">(your system asks for it)</span>}
              </label>
              <p className="pr-muted">The same look as on her page, kept in this browser only. Her world, hour and weather are set there.</p>
              <button type="button" className="sc-link" onClick={() => openPage('shiori')}>
                <ArrowUpRight size={16} aria-hidden="true" /> Open her page
              </button>
            </div>
          )}

          {tab === 'waiting' && (
            <div className="sc-pane">
              <Queue world={world} />
            </div>
          )}
        </div>
      )}

      {showDock && (
        <footer className="sc-dock" ref={dock}>
          {problem && (
            <p className="sc-problem" role="alert">
              {problem}
            </p>
          )}
          <div className="sc-modes" role="group" aria-label="Mode">
            {MODE_ORDER.map((m) => (
              <button key={m} type="button" aria-pressed={modeOf(mode) === m} onClick={() => switchMode(apiMode(m))} disabled={ended}>
                {MODES[m].label}
              </button>
            ))}
          </div>
          <form
            className="sc-compose"
            onSubmit={(e) => {
              e.preventDefault()
              void submit()
            }}
          >
            <button
              type="button"
              className={`sc-mic${ears.ears === 'recording' ? ' is-on' : ''}`}
              aria-label="Hold to talk"
              title="Hold to talk"
              disabled={ended || ears.ears === 'unavailable'}
              onPointerDown={(e) => {
                e.currentTarget.setPointerCapture(e.pointerId)
                mouth.stop()
                void ears.start()
              }}
              onPointerUp={() => ears.stop()}
              onPointerCancel={() => ears.cancel()}
            >
              <Mic size={16} aria-hidden="true" />
            </button>
            <button
              type="button"
              className={`sc-mic${byName ? ' is-on' : ''}`}
              aria-pressed={byName}
              aria-label="Listen for her name"
              title={byName ? 'Listening for “Shiori…”: click to stop' : 'Listen for her name: say “Shiori…” instead of holding the mic'}
              onClick={() => onByName(!byName)}
              disabled={ended}
            >
              <Ear size={16} aria-hidden="true" />
            </button>
            <label className="sr-only" htmlFor="sc-text">
              Message to Shiori
            </label>
            <input id="sc-text" value={text} onChange={(e) => setText(e.target.value)} placeholder={ended ? 'This session has ended' : 'Write to Shiori'} disabled={ended} autoComplete="off" />
            <button type="submit" className="sc-send" disabled={busy || !text.trim() || ended} aria-label="Send">
              <Send size={16} aria-hidden="true" />
            </button>
          </form>
          {!ended && (byNameShown || ears.ears !== 'idle') && (
            <p className={`sp-ears is-${byNameShown ? wake : ears.ears}`} aria-live="polite">
              {byNameShown ? wakeLabel(wake) : earsLabel(ears.ears, ears.ms)}
              {byNameShown && wake === 'talking' && (
                <button type="button" className="sp-ears-end" onClick={endTalk}>
                  Stop talking
                </button>
              )}
            </p>
          )}
        </footer>
      )}
    </div>
  )
}
