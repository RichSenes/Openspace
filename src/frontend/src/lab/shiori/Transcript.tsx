import { Check, FileText } from 'lucide-react'
import { useLayoutEffect, useRef, useState } from 'react'
import { MODE_ICONS } from './icons'
import { MODES, type Mode } from './modes'
import type { Entry, Token } from './script'

export function Cite({ path }: { path: string }) {
  const cut = path.lastIndexOf('/')
  return (
    <button type="button" className="cite" title={`Open ${path}`}>
      <FileText size={12} strokeWidth={2.2} aria-hidden="true" />
      <span className="cite-folder">{path.slice(0, cut + 1)}</span>
      <span className="cite-name">{path.slice(cut + 1)}</span>
    </button>
  )
}

function Words({ tokens, shown, streaming }: { tokens: Token[]; shown: number; streaming: boolean }) {
  return (
    <p className="say">
      {tokens.slice(0, shown).map((t, i) => (
        <span key={i} className={`tok${i >= shown - 3 && streaming ? ' is-new' : ''}`}>
          {i > 0 && !t.glued ? ' ' : ''}
          {t.type === 'word' ? t.text : <Cite path={t.path} />}
        </span>
      ))}
      {streaming && <span className="caret" aria-hidden="true" />}
    </p>
  )
}

interface Props {
  entries: Entry[]
  mode: Mode
  onSwitch: (m: Mode) => void
}

export function Transcript({ entries, mode, onSwitch }: Props) {
  const box = useRef<HTMLDivElement>(null)
  const stick = useRef(true)
  const [approved, setApproved] = useState<Record<string, boolean>>({})
  const last = entries.at(-1)
  const progress = last ? `${entries.length}:${last.kind === 'shiori' ? last.shown + (last.thinking ? 't' : '') : ''}` : ''

  // Follow the conversation unless the person scrolled up to read.
  useLayoutEffect(() => {
    const el = box.current
    if (el && stick.current) el.scrollTop = el.scrollHeight
  }, [progress])

  return (
    <div
      ref={box}
      className="transcript"
      role="log"
      aria-label="Conversation with Shiori"
      onScroll={(e) => {
        const el = e.currentTarget
        stick.current = el.scrollHeight - el.scrollTop - el.clientHeight < 40
      }}
    >
      <div className="transcript-start">
        <span>Session started at 09:12</span>
        <span className="hint">Saved in Sessions/ as you go</span>
      </div>
      {entries.map((e) => {
        if (e.kind === 'user') {
          return (
            <div key={e.id} className="msg msg-you">
              <span className="who">You</span>
              <p className="say">{e.text}</p>
            </div>
          )
        }
        if (e.kind === 'mode') {
          const Icon = MODE_ICONS[e.mode]
          return (
            <div key={e.id} className={`mode-line mode-line-${e.mode}`} role="note">
              <span className="mode-line-rule" aria-hidden="true" />
              <span className="mode-line-text">
                <Icon size={14} aria-hidden="true" />
                <span>
                  <strong>You switched to {MODES[e.mode].label}</strong>
                  <span className="mode-line-sub">{MODES[e.mode].switched}</span>
                </span>
              </span>
              <span className="mode-line-rule" aria-hidden="true" />
            </div>
          )
        }
        const done = !e.thinking && !e.streaming
        const offered = e.offer
        const offerTaken = offered ? MODES[mode].power >= MODES[offered].power : false
        return (
          <div key={e.id} className={`msg msg-shiori${e.thinking ? ' is-thinking' : ''}`}>
            <span className="who">Shiori</span>
            {e.thinking ? (
              <div className="reading" aria-live="polite">
                <span className="reading-dots" aria-hidden="true">
                  <i />
                  <i />
                  <i />
                </span>
                <span>{e.reads.length ? 'Reading' : 'Thinking'}</span>
                {e.reads.map((r, i) => (
                  <span key={r} className="reading-chip" style={{ animationDelay: `${i * 0.35}s` }}>
                    <Cite path={r} />
                  </span>
                ))}
              </div>
            ) : (
              <Words tokens={e.tokens} shown={e.shown} streaming={e.streaming} />
            )}
            {done && offered && (
              <div className="offer">
                {offerTaken ? (
                  <span className="offer-done">
                    <Check size={14} aria-hidden="true" /> Switched to {MODES[offered].label}
                  </span>
                ) : (
                  <button type="button" className={`btn offer-btn mode-${offered}`} onClick={() => onSwitch(offered)}>
                    {offered === 'decide' ? 'Ready to decide?' : 'Ready to plan?'} Switch to {MODES[offered].label}
                  </button>
                )}
              </div>
            )}
            {done && e.approve && (
              <div className="plan">
                <div className="plan-head">
                  <strong>Plan: 3 tasks in 2 offices</strong>
                  <span className="hint">Nothing starts until you approve it</span>
                </div>
                {approved[e.id] ? (
                  <span className="offer-done">
                    <Check size={14} aria-hidden="true" /> Approved: 3 tasks queued in Backend and Frontend
                  </span>
                ) : (
                  <div className="plan-actions">
                    <button type="button" className="btn btn-primary" onClick={() => setApproved((a) => ({ ...a, [e.id]: true }))}>
                      Approve plan
                    </button>
                  </div>
                )}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
