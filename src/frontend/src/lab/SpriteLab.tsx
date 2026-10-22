import { useEffect, useMemo, useState } from 'react'
import { toCanvas } from '../art/canvas'
import { CLOTH_COLORS, HAIR_COLORS, SKIN_TONES, THEMES, type ClothColor, type HairColor, type SkinTone, type ThemeName } from '../art/palette'
import { FACES, HAIR_STYLES, type Emotion, type HairStyle } from '../art/sheets/character'
import { DESK, FLOOR, WALL } from '../art/sheets/room'
import { pixelsOf, type Pixel, type SpriteDef } from '../art/sprite'
import { STATION, stationPixels, themeColors, type Appearance, type ScreenState, type StationPose } from '../art/station'
import './lab.css'

const FPS = 5
const EMOTIONS = Object.keys(FACES).filter((e) => e !== 'blink') as Emotion[]

/** Shows a picture made of pixels, scaled by a whole number and never smoothed. */
function Pixels({ width, height, pixels, scale, label }: { width: number; height: number; pixels: Pixel[]; scale: number; label: string }) {
  const url = useMemo(() => toCanvas(width, height, pixels).toDataURL(), [width, height, pixels])
  return <img className="pixels" src={url} width={width * scale} height={height * scale} alt={label} title={label} />
}

/** A frame counter at the animation speed of the art style. */
function useTick() {
  const [tick, setTick] = useState(0)
  useEffect(() => {
    const timer = setInterval(() => setTick((t) => t + 1), 1000 / FPS)
    return () => clearInterval(timer)
  }, [])
  return tick
}

const SAMPLE: { look: Appearance; pose: (tick: number) => StationPose }[] = [
  {
    look: { skin: 'fair', hair: 'brown', hairStyle: 'short', cloth: 'blue' },
    pose: (t) => ({ emotion: 'focused', hands: 'typing', frame: t % 2, screen: 'on' }),
  },
  {
    look: { skin: 'deep', hair: 'black', hairStyle: 'bun', cloth: 'green' },
    pose: (t) => ({ emotion: t % 20 === 0 ? 'blink' : 'happy', hands: 'rest', frame: 0, screen: 'on' }),
  },
  {
    look: { skin: 'tan', hair: 'auburn', hairStyle: 'long', cloth: 'red' },
    pose: (t) => ({ emotion: t % 25 === 0 ? 'blink' : 'worried', hands: 'rest', frame: 0, screen: 'alert' }),
  },
]

function roomPixels(tick: number, theme: ThemeName): { width: number; height: number; pixels: Pixel[] } {
  const cols = 8
  const rows = 4
  const colors = themeColors(THEMES[theme])
  const pixels: Pixel[] = []
  for (let c = 0; c < cols; c++) pixels.push(...pixelsOf(WALL, 0, colors, c * 16, 0))
  for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) pixels.push(...pixelsOf(FLOOR, 0, colors, c * 16, 16 + r * 16))
  SAMPLE.forEach((s, i) => {
    const station = stationPixels(s.look, s.pose(tick + i * 7), THEMES[theme])
    pixels.push(...station.map((p) => ({ ...p, x: p.x + 6 + i * 40, y: p.y + 22 })))
  })
  return { width: cols * 16, height: 16 + rows * 16, pixels }
}

/**
 * Every sprite, animation and combination in one page, to review the art in one
 * place. Open the app with ?lab in the address.
 */
export function SpriteLab() {
  const tick = useTick()
  const [scale, setScale] = useState(4)
  const [theme] = useState<ThemeName>('cozy')
  const [hairStyle, setHairStyle] = useState<HairStyle>('short')
  const room = useMemo(() => roomPixels(tick, theme), [tick, theme])

  const combos: Appearance[] = useMemo(() => {
    const skins = Object.keys(SKIN_TONES) as SkinTone[]
    const hairs = Object.keys(HAIR_COLORS) as HairColor[]
    const cloths = Object.keys(CLOTH_COLORS) as ClothColor[]
    return skins.map((skin, i) => ({ skin, hair: hairs[i % hairs.length], hairStyle, cloth: cloths[i % cloths.length] }))
  }, [hairStyle])

  const pieces: { sprite: SpriteDef; screen?: ScreenState }[] = [
    { sprite: FLOOR },
    { sprite: WALL },
    { sprite: DESK, screen: 'off' },
    { sprite: DESK, screen: 'on' },
    { sprite: DESK, screen: 'alert' },
  ]

  return (
    <main className="lab">
      <header className="lab-head">
        <h1 className="pixel">Sprite lab</h1>
        <label>
          Zoom{' '}
          <select className="input" value={scale} onChange={(e) => setScale(Number(e.target.value))}>
            {[1, 2, 3, 4, 6, 8].map((s) => (
              <option key={s} value={s}>
                {s}x
              </option>
            ))}
          </select>
        </label>
        <label>
          Hair{' '}
          <select className="input" value={hairStyle} onChange={(e) => setHairStyle(e.target.value as HairStyle)}>
            {Object.keys(HAIR_STYLES).map((h) => (
              <option key={h} value={h}>
                {h}
              </option>
            ))}
          </select>
        </label>
      </header>

      <section>
        <h2>Room, animated ({FPS} fps)</h2>
        <Pixels {...room} scale={scale} label="Room" />
      </section>

      <section>
        <h2>People × emotions</h2>
        <div className="lab-grid" style={{ gridTemplateColumns: `repeat(${EMOTIONS.length + 1}, auto)` }}>
          {combos.map((look) => (
            <div key={look.skin} className="lab-row">
              {[...EMOTIONS, 'blink' as const].map((emotion) => (
                <figure key={emotion}>
                  <Pixels
                    width={STATION.width}
                    height={STATION.height}
                    pixels={stationPixels(look, { emotion, hands: 'rest', frame: 0, screen: 'on' }, THEMES[theme])}
                    scale={Math.max(2, scale - 1)}
                    label={`${look.skin} ${emotion}`}
                  />
                  <figcaption>{emotion}</figcaption>
                </figure>
              ))}
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2>Pieces</h2>
        <div className="lab-pieces">
          {pieces.map(({ sprite, screen }, i) => (
            <figure key={i}>
              <Pixels
                width={sprite.width}
                height={sprite.height}
                pixels={pixelsOf(sprite, 0, themeColors(THEMES[theme], screen))}
                scale={scale + 2}
                label={sprite.name}
              />
              <figcaption>
                {sprite.name}
                {screen && ` (${screen})`}
              </figcaption>
            </figure>
          ))}
        </div>
      </section>
    </main>
  )
}
