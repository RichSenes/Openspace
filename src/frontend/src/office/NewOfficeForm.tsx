import { useState, type FormEvent } from 'react'
import { FileUp } from 'lucide-react'
import { useImportOffice, useOpenOffice } from '../api/hooks'
import type { OfficeSetup } from '../api/types'
import { useToast } from '../ui/toastContext'
import { describeSetup, readSetupFile } from './setupFile'
import { TEMPLATES, templateSetup } from './templates'
import './office.css'

type Start = 'empty' | 'file' | string

/**
 * Opens a new office: empty, from a template that brings rooms, roles and routines, or from an
 * office file exported elsewhere. Everything it brings can be changed afterwards.
 */
export function NewOfficeForm({ onOpened, onCancel, nameLabel = 'Office name' }: { onOpened: (id: string) => void; onCancel?: () => void; nameLabel?: string }) {
  const [name, setName] = useState('')
  const [start, setStart] = useState<Start>('empty')
  const [file, setFile] = useState<{ setup: OfficeSetup; fileName: string }>()
  const [fileProblem, setFileProblem] = useState<string>()
  const open = useOpenOffice()
  const importOffice = useImportOffice()
  const toast = useToast()
  const pending = open.isPending || importOffice.isPending
  const error = open.error ?? importOffice.error
  const template = TEMPLATES.find((t) => t.id === start)
  const needsName = start !== 'file'

  const done = (id: string, what: string) => {
    toast(what)
    setName('')
    setFile(undefined)
    setStart('empty')
    onOpened(id)
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const title = name.trim()
    if (start === 'empty') {
      open.mutate(title, { onSuccess: ({ id }) => done(id, `${title} opened`) })
    } else if (template) {
      importOffice.mutate({ setup: templateSetup(template, title), name: title }, { onSuccess: ({ id }) => done(id, `${title} opened from the ${template.label} template`) })
    } else if (file) {
      const chosen = title || file.setup.name
      importOffice.mutate({ setup: file.setup, name: chosen }, { onSuccess: ({ id }) => done(id, `${chosen} opened from ${file.fileName}`) })
    }
  }

  const choose = async (picked: File) => {
    const read = readSetupFile(await picked.text())
    if ('problem' in read) {
      setFile(undefined)
      setFileProblem(read.problem)
    } else {
      setFileProblem(undefined)
      setFile({ setup: read.setup, fileName: picked.name })
      setStart('file')
    }
  }

  return (
    <form className="new-office" onSubmit={submit}>
      <label className="field">
        <span>{nameLabel}</span>
        <input
          className="input"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder={start === 'file' && file ? file.setup.name : 'e.g. Shop website'}
          required={needsName}
          maxLength={80}
        />
        {start === 'file' && <span className="hint">Empty keeps the name in the file.</span>}
      </label>
      <fieldset className="field new-office-starts">
        <legend>Start from</legend>
        <label className="new-office-start">
          <input type="radio" name="start" checked={start === 'empty'} onChange={() => setStart('empty')} />
          <span>
            <strong>An empty office</strong>
            <span className="hint">You add the rooms and the desks.</span>
          </span>
        </label>
        {TEMPLATES.map((t) => (
          <label key={t.id} className="new-office-start">
            <input type="radio" name="start" checked={start === t.id} onChange={() => setStart(t.id)} />
            <span>
              <strong>{t.label}</strong>
              {start === t.id && <span className="hint">{t.description}</span>}
            </span>
          </label>
        ))}
        <div className="new-office-start">
          <input type="radio" name="start" checked={start === 'file'} disabled={!file} onChange={() => setStart('file')} aria-label="An office file" />
          <span>
            <strong>An office file</strong>
            <span className="hint">
              {file ? `${file.fileName}: ${describeSetup(file.setup)}. Roles you already have with the same name are kept as they are.` : 'Exported from an office, here or on another computer.'}
            </span>
            <label className="btn btn-quiet new-office-file">
              <FileUp size={16} aria-hidden /> {file ? 'Choose another file' : 'Choose an office file'}
              <input
                type="file"
                accept=".json,application/json"
                className="sr-only"
                onChange={(e) => {
                  const picked = e.target.files?.[0]
                  if (picked) void choose(picked)
                  e.target.value = ''
                }}
              />
            </label>
            {fileProblem && <span className="error">{fileProblem}</span>}
          </span>
        </div>
      </fieldset>
      {error && <p className="error">{error.message}</p>}
      <div className="row">
        <button type="submit" className="btn btn-primary" disabled={pending || (start === 'file' && !file)}>
          {start === 'empty' ? 'Open office' : start === 'file' ? 'Open from the file' : 'Open from the template'}
        </button>
        {onCancel && (
          <button type="button" className="btn btn-quiet" onClick={onCancel}>
            Cancel
          </button>
        )}
      </div>
    </form>
  )
}
