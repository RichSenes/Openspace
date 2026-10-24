import { useChangeRoomMood } from '../api/hooks'
import type { RoomView } from '../api/types'
import { THEMES, themeOf, type ThemeName } from '../art/themes'
import { useToast } from '../ui/toastContext'

interface Props {
  officeId: string
  /** The office's theme, which the room follows unless it has a mood of its own. */
  officeTheme: string
  room: RoomView
}

/** The look of one room, saved at once: any theme, or the office's. */
export function MoodPicker({ officeId, officeTheme, room }: Props) {
  const change = useChangeRoomMood(officeId)
  const toast = useToast()
  const mood = room.mood && room.mood in THEMES ? room.mood : ''

  return (
    <label className="field">
      <span>Mood of this room</span>
      <select
        className="input"
        value={mood}
        disabled={change.isPending}
        onChange={(e) => {
          const next = e.target.value || null
          change.mutate(
            { roomId: room.id, mood: next },
            { onSuccess: () => toast(`${room.name}: ${next ? THEMES[next as ThemeName].label : `like the office (${themeOf(officeTheme).label})`}`) },
          )
        }}
      >
        <option value="">Like the office ({themeOf(officeTheme).label})</option>
        {(Object.keys(THEMES) as ThemeName[]).map((t) => (
          <option key={t} value={t}>
            {THEMES[t].label}
            {themeOf(t).dark ? ' · lights always off' : ''}
          </option>
        ))}
      </select>
      {change.error && <span className="error">{change.error.message}</span>}
    </label>
  )
}
