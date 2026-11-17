import { useState, type FormEvent } from 'react'
import { Check, X } from 'lucide-react'
import { useEngines, useOfficeTasks, useOpenOffice } from '../api/hooks'
import type { OfficeView } from '../api/types'
import type { View } from '../layout/TopBar'
import { useToast } from '../ui/toastContext'
import { firstSteps, type StepAction } from './setup'
import './setup.css'

const OPENS: Record<StepAction, { label: string; view: View }> = {
  office: { label: 'Open office', view: 'building' },
  agents: { label: 'Open Agents', view: 'agents' },
  rooms: { label: 'Set up rooms', view: 'rooms' },
  repository: { label: 'Choose it in Office', view: 'office' },
  board: { label: 'Open the Board', view: 'board' },
}

interface Props {
  office: OfficeView | undefined
  onSelect: (officeId: string) => void
  onView: (view: View) => void
  /** Shown over the building: it can be put away. */
  onHide?: () => void
}

/** The path to a first office at work, each step with what it is for and where it is done. */
export function FirstSteps({ office, onSelect, onView, onHide }: Props) {
  const engines = useEngines()
  const tasks = useOfficeTasks(office?.id)
  const steps = firstSteps(engines.data, { office, tasks: tasks.data?.length ?? 0 })
  const next = steps.find((s) => !s.done)
  // Over the building only the next step says more: the rest is one line each.
  const open = (step: (typeof steps)[number]) => (onHide ? step === next : !step.done)

  return (
    <section className={onHide ? 'first-steps floating' : 'first-steps'} aria-label="First steps">
      <header className="first-steps-head">
        <div>
          <h2>{office ? `Getting ${office.name} to work` : 'Your first office'}</h2>
          <p className="hint">
            {steps.filter((s) => s.done).length} of {steps.length} done
          </p>
        </div>
        {onHide && (
          <button type="button" className="btn btn-quiet" onClick={onHide} title="Hide the first steps for this office">
            <X size={16} aria-hidden /> Hide
          </button>
        )}
      </header>
      <ol>
        {steps.map((step) => (
          <li key={step.action} className={step.done ? 'done' : step === next ? 'next' : undefined}>
            <span className="first-step-mark" aria-hidden>
              {step.done ? <Check size={14} /> : null}
            </span>
            <div className="first-step-body">
              <strong>{step.title}</strong>
              {(!onHide || step === next) && <span className="hint">{step.detail}</span>}
              {open(step) &&
                (step.action === 'office' ? (
                  <OpenOffice onOpened={onSelect} />
                ) : (
                  <button type="button" className={step === next ? 'btn btn-primary' : 'btn'} onClick={() => onView(OPENS[step.action].view)}>
                    {OPENS[step.action].label}
                  </button>
                ))}
            </div>
            <span className="sr-only">{step.done ? 'Done' : 'To do'}</span>
          </li>
        ))}
      </ol>
    </section>
  )
}

function OpenOffice({ onOpened }: { onOpened: (id: string) => void }) {
  const [name, setName] = useState('')
  const open = useOpenOffice()
  const toast = useToast()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    open.mutate(name, {
      onSuccess: ({ id }) => {
        toast(`${name.trim()} opened`)
        onOpened(id)
      },
    })
  }
  return (
    <form className="first-step-form" onSubmit={submit}>
      <label className="field">
        <span>Office name, usually the project&apos;s</span>
        <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Shop website" required />
      </label>
      <button type="submit" className="btn btn-primary" disabled={open.isPending}>
        Open office
      </button>
      {open.error && <p className="error">{open.error.message}</p>}
    </form>
  )
}
