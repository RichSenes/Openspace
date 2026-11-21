import { Check, X } from 'lucide-react'
import { useEngines, useOfficeTasks } from '../api/hooks'
import type { OfficeView } from '../api/types'
import type { View } from '../layout/TopBar'
import { NewOfficeForm } from '../office/NewOfficeForm'
import { requestSettingsSection } from '../settings/sections'
import { firstSteps, type StepAction } from './setup'
import './setup.css'

const OPENS: Record<StepAction, { label: string; view: View }> = {
  office: { label: 'Open office', view: 'building' },
  engine: { label: 'Open Settings › Engines', view: 'settings' },
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
                  <button type="button" className={step === next ? 'btn btn-primary' : 'btn'} onClick={() => {
                      if (step.action === 'engine') requestSettingsSection('engines')
                      onView(OPENS[step.action].view)
                    }}>
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
  return <NewOfficeForm onOpened={onOpened} nameLabel="Office name, usually the project's" />
}
