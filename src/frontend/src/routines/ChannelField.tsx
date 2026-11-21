import { useRoutineChannels } from '../api/hooks'
import { openSettings } from '../settings/sections'
import { settingsProblem } from './schedule'
import { channelOf, withText } from './settings'

/** The Telegram bot a routine writes with, chosen among the bots set up in Settings, never typed by hand. */
export function ChannelField({ settings, onChange }: { settings: string; onChange: (settings: string) => void }) {
  const channels = useRoutineChannels()
  const known = channels.data ?? []
  const channel = channelOf(settings)
  const missing = channel !== '' && !known.includes(channel)
  return (
    <label className="field">
      <span>Telegram</span>
      <select className="input" value={channel} disabled={Boolean(settingsProblem(settings))} onChange={(e) => onChange(withText(settings, 'channel', e.target.value))}>
        <option value="">Nowhere: keep it in Openspace only</option>
        {known.map((c) => (
          <option key={c} value={c}>
            {c}
          </option>
        ))}
        {missing && <option value={channel}>{channel} (not set up)</option>}
      </select>
      <span className="hint">
        {channels.data && known.length === 0 ? 'No bots set up yet. ' : missing ? `${channel} is not among your bots. ` : 'The bot that delivers it, in its topic of your group. '}
        <button type="button" className="link-button" onClick={() => openSettings('connections')}>
          {known.length === 0 ? 'Set up Telegram in Settings › Connections' : 'Manage bots'}
        </button>
      </span>
    </label>
  )
}
