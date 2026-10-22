import { useEffect, useMemo, useState } from 'react'
import { Minus, Plus, Trash2 } from 'lucide-react'
import { useArrangeRoom } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { toCanvas } from '../art/canvas'
import { FURNITURE } from '../art/fanPack'
import type { PropName } from '../art/sheets/props'
import { pixelsOf } from '../art/sprite'
import { themeColors } from '../art/station'
import { themeOf, type Theme } from '../art/themes'
import { useToast } from '../ui/toastContext'
import {
  addPiece,
  currentArrangement,
  editRoom,
  FURNITURE_NAMES,
  furnitureLabel,
  layoutRequest,
  LIMITS,
  place,
  removePiece,
  resize,
  type Drafts,
} from './arrange'
import type { Selection } from './BuildingCanvas'

interface Props {
  /** The office as it will look once saved. */
  office: OfficeView
  /** The office as it is saved now. */
  saved: OfficeView
  drafts: Drafts
  onDrafts: (drafts: Drafts) => void
  selection: Selection | null
  onSelect: (selection: Selection | null) => void
  onClose: () => void
}

/** Pictures of every piece of furniture in a theme's colours, to pick from. */
function useThumbnails(theme: Theme): Record<PropName, string> {
  return useMemo(() => {
    const colors = themeColors(theme.colors)
    return Object.fromEntries(
      FURNITURE_NAMES.map((name) => {
        const { sprite } = FURNITURE[name]
        return [name, toCanvas(sprite.width, sprite.height, pixelsOf(sprite, 0, colors)).toDataURL()]
      }),
    ) as Record<PropName, string>
  }, [theme])
}

/** Beside the building while rooms are arranged: size, furniture, save or cancel. */
export function ArrangePanel({ office, saved, drafts, onDrafts, selection, onSelect, onClose }: Props) {
  const theme = themeOf(office.theme)
  const thumbnails = useThumbnails(theme)
  const arrangeRoom = useArrangeRoom(office.id)
  const toast = useToast()
  const [error, setError] = useState<string>()

  const room = office.rooms.find((r) => r.id === selection?.roomId)
  const arrangement = room && currentArrangement(office, drafts, room.id)
  const target = selection?.target
  const piece = target?.kind === 'piece' ? arrangement?.furniture[target.index] : undefined
  const desk = target?.kind === 'desk' ? room?.desks.find((d) => d.id === target.id) : undefined
  const changed = Object.keys(drafts).length
  const suggested = theme.furniture
  const others = FURNITURE_NAMES.filter((n) => !suggested.includes(n))

  const edit = (change: Parameters<typeof editRoom>[3]) => room && onDrafts(editRoom(office, drafts, room.id, change))

  // Arrows nudge what is picked, Delete takes furniture away, Escape lets go.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!room || !target || !arrangement) return
      if (e.target instanceof HTMLElement && e.target.closest('input, select, textarea')) return
      const step = e.shiftKey ? 8 : 1
      const moves: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }
      if (moves[e.key]) {
        e.preventDefault()
        const spot = target.kind === 'desk' ? arrangement.desks[target.id] : arrangement.furniture[target.index]
        if (spot) onDrafts(editRoom(office, drafts, room.id, (a) => place(a, target, spot.x + moves[e.key][0], spot.y + moves[e.key][1])))
      } else if ((e.key === 'Delete' || e.key === 'Backspace') && target.kind === 'piece') {
        e.preventDefault()
        onDrafts(editRoom(office, drafts, room.id, (a) => removePiece(a, target.index)))
        onSelect({ roomId: room.id })
      } else if (e.key === 'Escape') {
        onSelect({ roomId: room.id })
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [room, target, arrangement, office, drafts, onDrafts, onSelect])

  const save = async () => {
    setError(undefined)
    try {
      for (const [roomId, draft] of Object.entries(drafts))
        await arrangeRoom.mutateAsync({ roomId, layout: draft === 'auto' ? 'auto' : layoutRequest(draft) })
      toast(changed === 1 ? 'Room arranged' : `${changed} rooms arranged`)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The layout could not be saved.')
    }
  }

  const add = (name: PropName) => {
    if (!room || !arrangement) return
    edit((a) => addPiece(a, name))
    onSelect({ roomId: room.id, target: { kind: 'piece', index: arrangement.furniture.length } })
  }

  const isAuto = (roomId: string) => !saved.rooms.find((r) => r.id === roomId)?.width

  return (
    <aside className="arrange-panel" aria-label="Arrange rooms">
      {!room || !arrangement ? (
        <p className="hint">Click a room to arrange it.</p>
      ) : (
        <>
          <header>
            <h2>{room.name}</h2>
            <p className="hint">
              {desk
                ? `${desk.name}: drag it or use the arrows.`
                : piece
                  ? `${furnitureLabel(piece.name)}: drag it, use the arrows or remove it.`
                  : 'Pick a desk or a piece of furniture to move it.'}
            </p>
            {piece && target?.kind === 'piece' && (
              <button
                type="button"
                className="btn"
                onClick={() => {
                  edit((a) => removePiece(a, target.index))
                  onSelect({ roomId: room.id })
                }}
              >
                <Trash2 size={16} /> Remove {furnitureLabel(piece.name).toLowerCase()}
              </button>
            )}
          </header>

          <section aria-label="Size">
            <Stepper
              label="Width"
              value={arrangement.width}
              limits={LIMITS.width}
              onChange={(width) => edit((a) => resize(a, width, a.depth))}
            />
            <Stepper
              label="Depth"
              value={arrangement.depth}
              limits={LIMITS.depth}
              onChange={(depth) => edit((a) => resize(a, a.width, depth))}
            />
          </section>

          <section aria-label="Add furniture">
            <h3>Suits {theme.label.toLowerCase()}</h3>
            <Catalogue names={suggested} thumbnails={thumbnails} onAdd={add} />
            <h3>Everything else</h3>
            <Catalogue names={others} thumbnails={thumbnails} onAdd={add} />
          </section>

          <button
            type="button"
            className="btn"
            disabled={drafts[room.id] === 'auto' || (isAuto(room.id) && !drafts[room.id])}
            onClick={() => {
              onDrafts({ ...drafts, [room.id]: 'auto' })
              onSelect({ roomId: room.id })
            }}
          >
            Back to automatic layout
          </button>
        </>
      )}

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <footer>
        <button type="button" className="btn" onClick={onClose} disabled={arrangeRoom.isPending}>
          Cancel
        </button>
        <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={changed === 0 || arrangeRoom.isPending}>
          Save layout
        </button>
      </footer>
    </aside>
  )
}

function Stepper({ label, value, limits, onChange }: { label: string; value: number; limits: readonly [number, number]; onChange: (value: number) => void }) {
  return (
    <div className="stepper">
      <span>
        {label} <strong>{value}</strong> tiles
      </span>
      <button type="button" className="btn" aria-label={`Less ${label.toLowerCase()}`} disabled={value <= limits[0]} onClick={() => onChange(value - 1)}>
        <Minus size={16} />
      </button>
      <button type="button" className="btn" aria-label={`More ${label.toLowerCase()}`} disabled={value >= limits[1]} onClick={() => onChange(value + 1)}>
        <Plus size={16} />
      </button>
    </div>
  )
}

function Catalogue({ names, thumbnails, onAdd }: { names: PropName[]; thumbnails: Record<PropName, string>; onAdd: (name: PropName) => void }) {
  return (
    <ul className="catalogue">
      {names.map((name) => (
        <li key={name}>
          <button type="button" title={FURNITURE[name].hint} onClick={() => onAdd(name)}>
            <img src={thumbnails[name]} alt="" />
            <span>{furnitureLabel(name)}</span>
          </button>
        </li>
      ))}
    </ul>
  )
}
