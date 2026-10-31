import { useState, type FormEvent } from 'react'
import { useSaveWeather, useWeather } from '../api/hooks'
import { useToast } from '../ui/toastContext'
import { weatherWords } from '../weather/weather'

/** The town whose weather the office windows show; none keeps a sky that only follows the clock. */
export function WeatherCard() {
  const weather = useWeather()
  const saved = weather.data?.place ?? ''
  return (
    <section className="settings-card" aria-labelledby="weather">
      <h2 id="weather">Weather at the windows</h2>
      <p className="hint">
        The windows show the real weather of a town: clouds, fog, rain, snow and storms. Only the town&apos;s name and position go to Open-Meteo, a
        free weather service with no account; it is asked again every quarter of an hour.
      </p>
      {weather.data && <WeatherForm key={saved} saved={saved} />}
      {weather.data?.now && <p>Now in {weatherWords(weather.data.now)}.</p>}
      {weather.data?.place && !weather.data.now && <p className="hint">The weather of {weather.data.place} could not be read yet: the windows stay clear.</p>}
    </section>
  )
}

function WeatherForm({ saved }: { saved: string }) {
  const [place, setPlace] = useState(saved)
  const save = useSaveWeather()
  const toast = useToast()
  const store = (value: string) =>
    save.mutate(value, { onSuccess: (view) => toast(view.place ? `Windows now show the weather of ${view.place}` : 'Windows follow the clock only') })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    store(place)
  }
  return (
    <form className="row" onSubmit={submit}>
      <label className="field">
        <span>Town</span>
        <input className="input" value={place} maxLength={120} placeholder="Turin, Italy" onChange={(e) => setPlace(e.target.value)} />
      </label>
      <button type="submit" className="btn btn-primary" disabled={place.trim() === saved || place.trim() === '' || save.isPending}>
        Show its weather
      </button>
      {saved && (
        <button type="button" className="btn btn-quiet" disabled={save.isPending} onClick={() => store('')}>
          Stop showing the weather
        </button>
      )}
      {save.error && <p className="error">{save.error.message}</p>}
    </form>
  )
}
