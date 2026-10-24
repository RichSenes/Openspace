import { useEffect, useMemo, useState } from 'react'
import { AppWindow, Minus, Plus, Rows3, Scissors, Shirt, Trash2 } from 'lucide-react'
import { useSaveFloorPlan } from '../api/hooks'
import type { OfficeView } from '../api/types'
import { toCanvas } from '../art/canvas'
import { FURNITURE } from '../art/fanPack'
import type { PropName } from '../art/sheets/props'
import { pixelsOf } from '../art/sprite'
import { themeColors } from '../art/station'
import { themeOf, type Theme } from '../art/themes'
import { useToast } from '../ui/toastContext'
import {
  addCommon,
  addCut,
  addPiece,
  addWindow,
  BUILDING,
  currentArrangement,
  currentFloor,
  editFloor,
  editRoom,
  FURNITURE_NAMES,
  furnitureLabel,
  LIMITS,
  NO_DRAFTS,
  place,
  placeCommon,
  planRequest,
  removeCommon,
  removeCut,
  removePiece,
  removeWindow,
  resize,
  resizeFloor,
  setCut,
  tidyDesks,
  type Drafts,
  type Target,
} from './arrange'
import type { Selection } from './BuildingCanvas'
import { DOOR } from './layout'
import { LookDialog } from './LookDialog'
import { MoodPicker } from './MoodPicker'

/** As many cuts as the API takes. */
const MAX_CUTS = 12

interface Props {
  /** The office as it will look once saved. */
  office: OfficeView
  /** The office as it is saved now. */
  saved: OfficeView
  drafts: Drafts
  /** Changes the drafts, from the office as they show it. */
  onEdit: (change: (shown: OfficeView, drafts: Drafts) => Drafts) => void
  onSelect: (selection: Selection | null) => void
  selection: Selection | null
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

/** What the picked thing is and what can be done with it, in a line. */
function describe(target: Target | undefined, office: OfficeView, roomId: string, pieceName: string | undefined): string {
  if (!target) return roomId === BUILDING ? 'Pick a piece of furniture or a cut to move it, or drag the yellow handles to resize the building.' : 'Pick a desk, a piece of furniture, the door or a window. Drag the back wall to move the room.'
  switch (target.kind) {
    case 'desk':
      return `${office.rooms.find((r) => r.id === roomId)?.desks.find((d) => d.id === target.id)?.name ?? 'Desk'}: drag it or use the arrows.`
    case 'piece':
      return `${furnitureLabel(pieceName ?? '')}: drag it, use the arrows or remove it.`
    case 'door':
      return 'Door: drag it along any wall of the room, or use the arrows.'
    case 'window':
      return 'Window: drag it along the back wall. It shows the sky on an outer wall, what is behind on an inner one.'
    case 'cut':
      return 'Cut: drag it, or its yellow handles, to shape the building. At an edge it makes an L, T or U, inside a courtyard.'
  }
}

/** Beside the building while it is arranged: sizes, furniture, doors and windows, save or cancel. */
export function ArrangePanel({ office, saved, drafts, onEdit, selection, onSelect, onClose }: Props) {
  const shownRoom = office.rooms.find((r) => r.id === selection?.roomId)
  // Furniture is suggested and shown in the colours of the room it goes in.
  const theme = themeOf(shownRoom?.mood ?? office.theme)
  const thumbnails = useThumbnails(theme)
  const savePlan = useSaveFloorPlan(office.id)
  const toast = useToast()
  const [error, setError] = useState<string>()
  const [dressing, setDressing] = useState<string>()

  const building = selection?.roomId === BUILDING
  const room = building ? undefined : office.rooms.find((r) => r.id === selection?.roomId)
  const arrangement = room && currentArrangement(office, drafts, room.id)
  const floor = building ? currentFloor(office, drafts) : undefined
  const target = selection?.target
  const cutIndex = building && target?.kind === 'cut' ? target.index : undefined
  const cut = cutIndex !== undefined ? floor?.cuts[cutIndex] : undefined
  const pieceName =
    target?.kind === 'piece' ? (building ? floor?.furniture[target.index]?.name : arrangement?.furniture[target.index]?.name) : undefined
  const changed = drafts !== NO_DRAFTS
  const suggested = theme.furniture
  const others = FURNITURE_NAMES.filter((n) => !suggested.includes(n))

  const editThisRoom = (change: Parameters<typeof editRoom>[3]) => room && onEdit((o, d) => editRoom(o, d, room.id, change))
  const editTheFloor = (change: Parameters<typeof editFloor>[2]) => onEdit((o, d) => editFloor(o, d, change))

  const remove = () => {
    if (!selection || !target) return
    if (building && target.kind === 'piece') editTheFloor((f) => removeCommon(f, target.index))
    else if (building && target.kind === 'cut') editTheFloor((f) => removeCut(f, target.index))
    else if (target.kind === 'piece') editThisRoom((a) => removePiece(a, target.index))
    else if (target.kind === 'window') editThisRoom((a) => removeWindow(a, target.index))
    else return
    onSelect({ roomId: selection.roomId })
  }

  // Arrows nudge what is picked, Delete takes furniture and windows away, Escape lets go.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!selection || !target) return
      if (e.target instanceof HTMLElement && e.target.closest('input, select, textarea, dialog')) return
      const step = e.shiftKey ? 8 : 1
      const moves: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }
      const move = moves[e.key]
      if (move) {
        e.preventDefault()
        const [dx, dy] = move
        if (building && target.kind === 'piece') {
          const piece = floor?.furniture[target.index]
          if (piece) editTheFloor((f) => placeCommon(f, target.index, piece.x + dx, piece.y + dy))
        } else if (building && target.kind === 'cut') {
          // A tile at a time, as rooms move.
          const cut = floor?.cuts[target.index]
          if (cut) editTheFloor((f, sizes) => setCut(f, sizes, target.index, { ...cut, left: cut.left + Math.sign(dx), top: cut.top + Math.sign(dy) }))
        } else if (arrangement && target.kind === 'door') {
          // Along its wall, a tile at a time.
          const along = arrangement.door.wall === 'Left' || arrangement.door.wall === 'Right' ? Math.sign(dy) : Math.sign(dx)
          const length = arrangement.door.wall === 'Left' || arrangement.door.wall === 'Right' ? arrangement.depth : arrangement.width
          editThisRoom((a) => ({ ...a, door: { ...a.door, offset: Math.max(0, Math.min(length - DOOR, a.door.offset + along)) } }))
        } else if (arrangement) {
          const spot =
            target.kind === 'desk'
              ? arrangement.desks[target.id]
              : target.kind === 'piece'
                ? arrangement.furniture[target.index]
                : target.kind === 'window'
                  ? { x: arrangement.windows[target.index], y: 0 }
                  : undefined
          if (spot) editThisRoom((a) => place(a, target, spot.x + dx, spot.y + dy))
        }
      } else if (e.key === 'Delete' || e.key === 'Backspace') {
        e.preventDefault()
        remove()
      } else if (e.key === 'Escape') {
        onSelect({ roomId: selection.roomId })
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  const save = async () => {
    setError(undefined)
    try {
      await savePlan.mutateAsync(drafts.auto ? 'auto' : planRequest(office))
      toast(drafts.auto ? `${office.name} is laid out automatically again` : `${office.name} arranged`)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The layout could not be saved.')
    }
  }

  const add = (name: PropName) => {
    if (building && floor) {
      editTheFloor((f, sizes) => addCommon(f, sizes, name))
      onSelect({ roomId: BUILDING, target: { kind: 'piece', index: floor.furniture.length } })
    } else if (room && arrangement) {
      editThisRoom((a) => addPiece(a, name))
      onSelect({ roomId: room.id, target: { kind: 'piece', index: arrangement.furniture.length } })
    }
  }

  const planned = Boolean(saved.floorWidth && saved.floorDepth)

  return (
    <aside className="arrange-panel" aria-label="Arrange the building">
      {!selection || (!building && !arrangement) ? (
        <p className="hint">Click a room to arrange it, or an empty spot of the corridors for the building itself.</p>
      ) : (
        <>
          <header>
            <h2>{building ? 'Building' : room?.name}</h2>
            <p className="hint">{describe(target, office, selection.roomId, pieceName)}</p>
            {(target?.kind === 'piece' || target?.kind === 'window' || target?.kind === 'cut') && (
              <button type="button" className="btn" onClick={remove}>
                <Trash2 size={16} />{' '}
                {target.kind === 'cut' ? 'Fill the cut back in' : `Remove ${target.kind === 'window' ? 'window' : furnitureLabel(pieceName ?? '').toLowerCase()}`}
              </button>
            )}
            {target?.kind === 'desk' && (
              <button type="button" className="btn" onClick={() => setDressing(target.id)}>
                <Shirt size={16} /> Change look
              </button>
            )}
          </header>

          <section aria-label="Size">
            {building && floor && cut ? (
              <>
                <Stepper label="Cut width" value={cut.width} limits={[1, floor.width - cut.left]} onChange={(w) => editTheFloor((f, s) => setCut(f, s, cutIndex!, { ...cut, width: w }))} />
                <Stepper label="Cut depth" value={cut.height} limits={[1, floor.depth - cut.top]} onChange={(h) => editTheFloor((f, s) => setCut(f, s, cutIndex!, { ...cut, height: h }))} />
              </>
            ) : building && floor ? (
              <>
                <Stepper label="Width" value={floor.width} limits={LIMITS.floorWidth} onChange={(w) => editTheFloor((f, s) => resizeFloor(f, s, w, f.depth))} />
                <Stepper label="Depth" value={floor.depth} limits={LIMITS.floorDepth} onChange={(d) => editTheFloor((f, s) => resizeFloor(f, s, f.width, d))} />
              </>
            ) : (
              arrangement && (
                <>
                  <Stepper label="Width" value={arrangement.width} limits={LIMITS.width} onChange={(w) => editThisRoom((a) => resize(a, w, a.depth))} />
                  <Stepper label="Depth" value={arrangement.depth} limits={LIMITS.depth} onChange={(d) => editThisRoom((a) => resize(a, a.width, d))} />
                </>
              )
            )}
          </section>

          {building && floor && (
            <section className="arrange-actions" aria-label="Shape">
              <button
                type="button"
                className="btn"
                disabled={floor.cuts.length >= MAX_CUTS}
                onClick={() => {
                  editTheFloor(addCut)
                  onSelect({ roomId: BUILDING, target: { kind: 'cut', index: floor.cuts.length } })
                }}
              >
                <Scissors size={16} /> Cut a corner
              </button>
            </section>
          )}

          {room && <MoodPicker officeId={office.id} officeTheme={office.theme} room={room} />}

          {room && arrangement && (
            <section className="arrange-actions" aria-label="Room">
              <button
                type="button"
                className="btn"
                onClick={() => {
                  editThisRoom(addWindow)
                  onSelect({ roomId: room.id, target: { kind: 'window', index: arrangement.windows.length } })
                }}
              >
                <AppWindow size={16} /> Add a window
              </button>
              <button type="button" className="btn" onClick={() => editThisRoom(tidyDesks)}>
                <Rows3 size={16} /> Desks in rows
              </button>
            </section>
          )}

          <section aria-label="Add furniture">
            <h3>{building ? 'For the corridors' : `Suits ${theme.label.toLowerCase()}`}</h3>
            <Catalogue names={building ? [...theme.hall, ...suggested.filter((n) => !theme.hall.includes(n))] : suggested} thumbnails={thumbnails} onAdd={add} />
            <h3>Everything else</h3>
            <Catalogue names={building ? FURNITURE_NAMES.filter((n) => !theme.hall.includes(n) && !suggested.includes(n)) : others} thumbnails={thumbnails} onAdd={add} />
          </section>

          {building && (
            <button
              type="button"
              className="btn"
              disabled={drafts.auto || (!planned && !changed)}
              onClick={() => {
                onEdit(() => ({ ...NO_DRAFTS, auto: true }))
                onSelect({ roomId: BUILDING })
              }}
            >
              Back to the automatic building
            </button>
          )}
        </>
      )}

      <LookDialog
        officeId={office.id}
        theme={office.theme}
        desk={saved.rooms.flatMap((r) => r.desks).find((d) => d.id === dressing)}
        onClose={() => setDressing(undefined)}
      />
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <footer>
        <button type="button" className="btn" onClick={onClose} disabled={savePlan.isPending}>
          Cancel
        </button>
        <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={!changed || savePlan.isPending}>
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
