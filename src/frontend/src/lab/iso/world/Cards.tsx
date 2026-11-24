import { useState } from 'react'
import { useAnswerPermission, useApproveTask, useDecideProposal, useSendBack, useTaskCommand, useTaskLog } from '../../../api/hooks'
import type { WorldCharacter, WorldView, WorldWaiting } from '../../../api/types'
import { openPage } from './pages'
import type { Picked } from './world'

/**
 * The cards that open over the lab: a character's (who it is, what it does, what it brings to
 * Shiori and the answers to give) and Shiori's (everyone in her queue, each with its answers).
 * The lab stays underneath; a link opens Openspace's own page when more is needed.
 */

const KIND: Record<WorldCharacter['kind'], string> = { Desk: 'Agent at a desk', Routine: 'Routine', Intern: 'Intern' }

const STATE: Record<WorldCharacter['state'], string> = {
  Free: 'Free: taking a break',
  Working: 'At work',
  Waiting: 'Waiting for you',
  Failed: 'Stuck: something went wrong',
  Off: 'Switched off',
}

const NEED: Record<string, string> = {
  Permission: 'Asks permission',
  Question: 'Asks you something',
  TaskToReview: 'Work to review',
  TaskToApprove: 'A task to approve',
  TaskFailed: 'A task that failed',
  Proposal: 'A proposal',
  RoutineFailed: 'Its last run failed',
  DecisionToReview: 'A decision Shiori took',
  DecisionToAccept: 'A decision for you',
  DecisionDue: 'A decision to look at again',
  Agreement: 'Two desks need you to choose',
}

function Answers({ need }: { need: WorldWaiting }) {
  const permission = useAnswerPermission()
  const command = useTaskCommand()
  const approve = useApproveTask()
  const sendBack = useSendBack()
  const proposal = useDecideProposal()
  const [feedback, setFeedback] = useState('')
  const [writing, setWriting] = useState(false)
  const busy = permission.isPending || command.isPending || approve.isPending || sendBack.isPending || proposal.isPending
  const done = permission.isSuccess || command.isSuccess || approve.isSuccess || sendBack.isSuccess || proposal.isSuccess
  if (done) return <p className="card-done">Done: they will take it from here.</p>
  switch (need.kind) {
    case 'Permission':
      return (
        <div className="card-actions">
          <button type="button" disabled={busy} onClick={() => permission.mutate({ taskId: need.id, allow: true })}>
            Allow
          </button>
          <button type="button" disabled={busy} onClick={() => permission.mutate({ taskId: need.id, allow: false })}>
            Deny
          </button>
        </div>
      )
    case 'TaskToReview':
      return writing ? (
        <form
          className="card-actions"
          onSubmit={(e) => {
            e.preventDefault()
            if (feedback.trim()) sendBack.mutate({ taskId: need.id, text: feedback.trim(), withReview: false })
          }}
        >
          <label className="sr-only" htmlFor={`fb-${need.id}`}>
            What to change
          </label>
          <input id={`fb-${need.id}`} value={feedback} onChange={(e) => setFeedback(e.target.value)} placeholder="What to change" />
          <button type="submit" disabled={busy || !feedback.trim()}>
            Send back
          </button>
          <button type="button" onClick={() => setWriting(false)}>
            Cancel
          </button>
        </form>
      ) : (
        <div className="card-actions">
          <button type="button" disabled={busy} onClick={() => command.mutate({ taskId: need.id, command: 'accept' })}>
            Accept
          </button>
          <button type="button" disabled={busy} onClick={() => setWriting(true)}>
            Send back…
          </button>
        </div>
      )
    case 'TaskToApprove':
      return (
        <div className="card-actions">
          <button type="button" disabled={busy} onClick={() => approve.mutate({ taskId: need.id, deskId: need.characterId })}>
            Approve
          </button>
          <button type="button" disabled={busy} onClick={() => command.mutate({ taskId: need.id, command: 'discard' })}>
            Discard
          </button>
        </div>
      )
    case 'TaskFailed':
      return (
        <div className="card-actions">
          <button type="button" disabled={busy} onClick={() => command.mutate({ taskId: need.id, command: 'retry' })}>
            Try again
          </button>
          <button type="button" disabled={busy} onClick={() => command.mutate({ taskId: need.id, command: 'discard' })}>
            Let it go
          </button>
        </div>
      )
    case 'Proposal':
      return (
        <div className="card-actions">
          <button type="button" disabled={busy} onClick={() => proposal.mutate({ id: need.id, approve: true })}>
            Accept
          </button>
          <button type="button" disabled={busy} onClick={() => proposal.mutate({ id: need.id, approve: false })}>
            Reject
          </button>
        </div>
      )
    default:
      return (
        <div className="card-actions">
          <button type="button" onClick={() => openPage(need.kind.startsWith('Decision') ? 'brain' : need.kind === 'RoutineFailed' ? 'routines' : 'board')}>
            Open
          </button>
        </div>
      )
  }
}

function Need({ need, who }: { need: WorldWaiting; who?: string }) {
  return (
    <li className="card-need">
      <p className="card-need-kind">
        {NEED[need.kind] ?? need.kind}
        {who && <span> · {who}</span>}
      </p>
      <p className="card-need-title">{need.title}</p>
      {need.detail && <p className="card-need-detail">{need.detail}</p>}
      <Answers need={need} />
    </li>
  )
}

function Log({ taskId }: { taskId: string }) {
  const log = useTaskLog(taskId, true)
  const lines = (log.data ?? []).slice(-4).reverse()
  if (!lines.length) return null
  return (
    <ul className="card-log" aria-label="Latest from its work">
      {lines.map((l) => (
        <li key={l.id}>{l.summary}</li>
      ))}
    </ul>
  )
}

export function Card({ picked, world, onClose }: { picked: Picked; world: WorldView | undefined; onClose: () => void }) {
  return (
    <aside className="world-card" aria-label={picked.kind === 'shiori' ? 'Shiori' : 'Character'}>
      <button type="button" className="card-close" onClick={onClose} aria-label="Close">
        ×
      </button>
      {picked.kind === 'shiori' ? <ShioriCard world={world} /> : <CharacterCard picked={picked} world={world} />}
    </aside>
  )
}

function CharacterCard({ picked, world }: { picked: Extract<Picked, { kind: 'agent' }>; world: WorldView | undefined }) {
  const office = world?.offices.find((o) => o.characters.some((c) => c.id === picked.id))
  const who = office?.characters.find((c) => c.id === picked.id)
  if (!who || !office) {
    return (
      <>
        <h2>{picked.name}</h2>
        <p className="card-sub">{picked.room} · a demo agent</p>
        <p className="card-state">Not one of your agents: turn on "Real offices" to see yours.</p>
      </>
    )
  }
  const needs = (world?.waiting ?? []).filter((w) => w.characterId === who.id)
  const helper = who.forDesk ? office.characters.find((c) => c.id === who.forDesk) : undefined
  return (
    <>
      <h2>{who.name}</h2>
      <p className="card-sub">
        {KIND[who.kind]} · {office.name}
        {helper && ` · helping ${helper.name}`}
      </p>
      <p className={`card-state is-${who.state.toLowerCase()}`}>
        {STATE[who.state]}
        {who.doing && who.state === 'Working' && `: ${who.doing}`}
      </p>
      {who.taskTitle && (
        <section className="card-task">
          <h3>Task</h3>
          <p>{who.taskTitle}</p>
          {who.taskId && <Log taskId={who.taskId} />}
        </section>
      )}
      {who.traits.length > 0 && (
        <ul className="card-traits" aria-label="Character">
          {who.traits.map((t) => (
            <li key={t}>{t.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()}</li>
          ))}
        </ul>
      )}
      {needs.length > 0 && (
        <section>
          <h3>Brings to Shiori</h3>
          <ul className="card-needs">
            {needs.map((n) => (
              <Need key={`${n.kind}${n.id}`} need={n} />
            ))}
          </ul>
        </section>
      )}
      <button type="button" className="card-link" onClick={() => openPage('board')}>
        Open the board
      </button>
    </>
  )
}

function ShioriCard({ world }: { world: WorldView | undefined }) {
  return (
    <>
      <h2>Shiori</h2>
      <p className="card-sub">The lab's mind</p>
      <button type="button" className="card-link is-primary" onClick={() => openPage('shiori')}>
        Talk to her
      </button>
      <Queue world={world} />
    </>
  )
}

/** Everyone waiting for the person at Shiori, each with who brings it and the answers to give. */
export function Queue({ world }: { world: WorldView | undefined }) {
  const names = new Map((world?.offices ?? []).flatMap((o) => o.characters.map((c) => [c.id, `${c.name}, ${o.name}`] as const)))
  const queue = world?.waiting ?? []
  return (
    <section>
      <h3>{queue.length ? `Waiting for you (${queue.length})` : 'Nobody is waiting for you'}</h3>
      <ul className="card-needs">
        {queue.map((n) => (
          <Need key={`${n.kind}${n.id}`} need={n} who={n.characterId ? names.get(n.characterId) : 'hers'} />
        ))}
      </ul>
    </section>
  )
}
