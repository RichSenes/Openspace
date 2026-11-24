import { Mic, Plus, Send } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useCreateShioriSession, useSendShioriMessage, useSetShioriMode, useShioriSession, useShioriSessions } from '../../../api/hooks'
import type { ShioriMode, WorldView } from '../../../api/types'
import { ShioriBody } from '../../presence/body/ShioriBody'
import { WORLDS } from '../../presence/world/worlds'
import { tokenize } from '../../shiori/script'
import { MODE_ORDER, MODES } from '../../shiori/modes'
import { useLive } from '../../../shiori/liveStore'
import { loadLook } from '../../../shiori/look'
import { nextSentences } from '../../../shiori/speech'
import { answerText, apiMode, faceFor, modeOf, orderSessions, pickSession, titleFrom } from '../../../shiori/talk'
import { useEars } from '../../../shiori/useEars'
import { useVoice } from '../../../shiori/useVoice'
import { afterHearing, type Heard } from '../../../shiori/voice'
import { Words } from '../../../shiori/Words'
import { usePersistentState } from '../../../usePersistentState'
import { Queue } from './Cards'
import '../../presence/presence.css'

/**
 * Shiori in the lab, compact: a small card of her in the top corner, her face moving as she
 * listens, thinks and answers; under it a plain chat (what you said, what she answered, her
 * answer arriving as she writes it), a box to write or hold to talk, the modes; tabs for her
 * sessions and for who waits for you. Answers to spoken words are spoken back.
 */

const NEW = 'new'
type Tab = 'chat' | 'sessions' | 'waiting'

export function ShioriChat({ world, pending, onPendingTaken, onClose }: { world: WorldView | undefined; pending: Heard | null; onPendingTaken: () => void; onClose: () => void }) {
  const [tab, setTab] = useState<Tab>('chat')
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
  const [look] = useState(loadLook)
  const log = useRef<HTMLOListElement>(null)

  const answering = !!view?.answering
  const busy = answering || send.isPending || create.isPending
  const ended = !!view?.endedAt
  const mode = view?.mode ?? nextMode
  const entries = view?.transcript ?? []
  const latest = answerText(entries, live.draft, answering)

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
  useEffect(() => {
    submitRef.current = submit
  })
  const onHeard = (heard: Heard) => {
    if (heard.language) heardIn.current = heard.language
    const next = afterHearing(heard, text)
    setText(next.compose)
    if (next.send) void submitRef.current(next.send)
  }
  const ears = useEars(onHeard, (m) => setProblem(m))

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
  }, [entries.length, live.draft, tab])

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
  const waiting = world?.waiting.length ?? 0

  return (
    <div className="sc" data-mode={modeOf(mode)} style={{ '--mode': MODES[modeOf(mode)].hex } as React.CSSProperties}>
      <header className="sc-head">
        <div className="sc-tabs" role="tablist" aria-label="Shiori">
          {(['chat', 'sessions', 'waiting'] as Tab[]).map((t) => (
            <button key={t} type="button" role="tab" aria-selected={tab === t} onClick={() => setTab(t)}>
              {t === 'chat' ? 'Chat' : t === 'sessions' ? 'Sessions' : `Waiting${waiting ? ` (${waiting})` : ''}`}
            </button>
          ))}
          <button type="button" className="sc-close" onClick={onClose} aria-label="Close">
            ×
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
          {/* Her, small: a card in the corner, her face following the conversation. */}
          <div className="sc-face" aria-hidden="true">
            <ShioriBody state={face} skin={look.skin} mode={modeOf(mode)} pixel={look.pixel} palette={WORLDS[look.world].palette} calm={look.calm} focus={{ x: 0.5, y: 0.5, size: 28 }} notes={[]} />
          </div>
        </div>
      </header>

      {tab === 'chat' && (
        <>
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
          <footer className="sc-dock">
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
                disabled={ended || ears.ears === 'unavailable'}
                onPointerDown={() => {
                  mouth.stop()
                  void ears.start()
                }}
                onPointerUp={() => ears.stop()}
                onPointerLeave={() => ears.ears === 'recording' && ears.stop()}
              >
                <Mic size={16} aria-hidden="true" />
              </button>
              <label className="sr-only" htmlFor="sc-text">
                Message to Shiori
              </label>
              <input id="sc-text" value={text} onChange={(e) => setText(e.target.value)} placeholder={ended ? 'This session has ended' : 'Write to Shiori'} disabled={ended} autoComplete="off" />
              <button type="submit" className="sc-send" disabled={busy || !text.trim() || ended} aria-label="Send">
                <Send size={16} aria-hidden="true" />
              </button>
            </form>
          </footer>
        </>
      )}

      {tab === 'sessions' && (
        <div className="sc-pane">
          <button
            type="button"
            className="sc-new"
            onClick={() => {
              setWanted(NEW)
              setTab('chat')
            }}
          >
            <Plus size={14} aria-hidden="true" /> New conversation
          </button>
          <ul className="sc-sessions">
            {orderSessions(sessions.data ?? []).map((s) => (
              <li key={s.id}>
                <button
                  type="button"
                  aria-current={s.id === id}
                  onClick={() => {
                    setWanted(s.id)
                    setTab('chat')
                  }}
                >
                  <span>{s.title}</span>
                  {s.endedAt && <span className="sc-ended">ended</span>}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {tab === 'waiting' && (
        <div className="sc-pane">
          <Queue world={world} />
        </div>
      )}
    </div>
  )
}
