import { useEffect, useMemo, useState } from 'react'
import { AGENT_LOOKS, poseOf, type AgentState } from '../art/agentStates'
import { toCanvas } from '../art/canvas'
import { CLOTH_COLORS, HAIR_COLORS, SKIN_TONES, THEMES, type ClothColor, type HairColor, type SkinTone, type ThemeName } from '../art/palette'
import { scenePixels, type SceneItem } from '../art/scene'
import { ACCESSORIES, FACES, HAIR_STYLES, type Accessory, type Emotion, type HairStyle } from '../art/sheets/character'
import { PROPS, type PropName } from '../art/sheets/props'
import { DESK, FLOOR, WALL } from '../art/sheets/room'
import { SIGNALS, type Signal } from '../art/sheets/signals'
import { pixelsOf, type Pixel, type SpriteDef } from '../art/sprite'
import { STATION, stationPixels, themeColors, type Appearance, type ScreenState } from '../art/station'
import './lab.css'

const FPS = 5

/** Rows at the top of a station kept free for signals; cropped where no signal is shown. */
const SIGNAL_ROOM = 11

const EMOTIONS = Object.keys(FACES).filter((e) => e !== 'blink') as Emotion[]
const STATES = Object.keys(AGENT_LOOKS) as AgentState[]

/** Shows a picture made of pixels, scaled by a whole number and never smoothed. */
function Pixels({ width, height, pixels, scale, label }: { width: number; height: number; pixels: Pixel[]; scale: number; label: string }) {
  const url = useMemo(() => toCanvas(width, height, pixels).toDataURL(), [width, height, pixels])
  return <img className="pixels" src={url} width={width * scale} height={height * scale} alt={label} title={label} />
}

/** A station without the empty room for signals above the head. */
function Station({ pixels, scale, label }: { pixels: Pixel[]; scale: number; label: string }) {
  const cropped = pixels.map((p) => ({ ...p, y: p.y - SIGNAL_ROOM }))
  return <Pixels width={STATION.width} height={STATION.height - SIGNAL_ROOM} pixels={cropped} scale={scale} label={label} />
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

const PEOPLE: Appearance[] = [
  { skin: 'fair', hair: 'brown', hairStyle: 'short', cloth: 'blue', accessory: 'headset' },
  { skin: 'medium', hair: 'black', hairStyle: 'long', cloth: 'purple', accessory: 'glasses' },
  { skin: 'tan', hair: 'auburn', hairStyle: 'bun', cloth: 'red' },
  { skin: 'deep', hair: 'black', hairStyle: 'short', cloth: 'green' },
  { skin: 'medium', hair: 'blonde', hairStyle: 'long', cloth: 'mustard', accessory: 'glasses' },
  { skin: 'fair', hair: 'grey', hairStyle: 'bun', cloth: 'grey' },
]

/** A sample room: two rows of desks among furniture, everything sorted by depth. */
function roomPixels(tick: number, theme: ThemeName): { width: number; height: number; pixels: Pixel[] } {
  const cols = 14
  const rows = 7
  const colors = themeColors(THEMES[theme])
  const floor: Pixel[] = []
  for (let c = 0; c < cols; c++) floor.push(...pixelsOf(WALL, 0, colors, c * 16, 0))
  for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) floor.push(...pixelsOf(FLOOR, 0, colors, c * 16, 16 + r * 16))

  const prop = (name: PropName, x: number, y: number): SceneItem => {
    const sprite = PROPS[name]
    return { x, y, height: sprite.height, pixels: pixelsOf(sprite, Math.floor(tick / 3), colors) }
  }
  const desk = (who: number, state: AgentState, x: number, y: number): SceneItem => ({
    x,
    y,
    height: STATION.height,
    pixels: stationPixels(PEOPLE[who], poseOf(state, tick + who * 7), THEMES[theme]),
  })
  const items: SceneItem[] = [
    prop('whiteboard', 8, 2),
    prop('serverRack', 200, 0),
    prop('floorLamp', 184, 6),
    desk(0, 'working', 52, 4),
    desk(1, 'reading', 98, 4),
    desk(2, 'waiting', 144, 4),
    desk(3, 'done', 30, 50),
    desk(4, 'failed', 76, 50),
    desk(5, 'queued', 122, 50),
    prop('plant', 4, 100),
    prop('plant', 204, 64),
    prop('sideTable', 176, 102),
  ]
  return { width: cols * 16, height: 16 + rows * 16, pixels: [...floor, ...scenePixels(items)] }
}

/**
 * Every sprite, animation and combination in one page, to review the art in one
 * place. Open the app with ?lab in the address.
 */
export function SpriteLab() {
  const tick = useTick()
  const [scale, setScale] = useState(4)
  const [theme, setTheme] = useState<ThemeName>('cozy')
  const [hairStyle, setHairStyle] = useState<HairStyle>('short')
  const room = useMemo(() => roomPixels(tick, theme), [tick, theme])
  const small = Math.max(2, scale - 1)

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
    ...Object.values(PROPS).map((sprite) => ({ sprite })),
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
          Theme{' '}
          <select className="input" value={theme} onChange={(e) => setTheme(e.target.value as ThemeName)}>
            {Object.keys(THEMES).map((t) => (
              <option key={t} value={t}>
                {t}
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
        <h2>Agent states</h2>
        <div className="lab-pieces">
          {STATES.map((state, i) => (
            <figure key={state}>
              <Pixels
                width={STATION.width}
                height={STATION.height}
                pixels={stationPixels(PEOPLE[i % PEOPLE.length], poseOf(state, tick + i * 3), THEMES[theme])}
                scale={small}
                label={state}
              />
              <figcaption>{state}</figcaption>
            </figure>
          ))}
        </div>
      </section>

      <section>
        <h2>People × emotions</h2>
        <div className="lab-grid" style={{ gridTemplateColumns: `repeat(${EMOTIONS.length + 1}, auto)` }}>
          {combos.map((look) => (
            <div key={look.skin} className="lab-row">
              {[...EMOTIONS, 'blink' as const].map((emotion) => (
                <figure key={emotion}>
                  <Station
                    pixels={stationPixels(look, { emotion, gesture: 'rest', frame: 0, screen: 'on' }, THEMES[theme])}
                    scale={small}
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
        <h2>Accessories</h2>
        <div className="lab-grid" style={{ gridTemplateColumns: `repeat(${combos.length}, auto)` }}>
          {(Object.keys(ACCESSORIES) as Accessory[]).map((accessory) => (
            <div key={accessory} className="lab-row">
              {combos.map((look) => (
                <figure key={look.skin}>
                  <Station
                    pixels={stationPixels({ ...look, accessory }, { emotion: 'neutral', gesture: 'rest', frame: 0, screen: 'on' }, THEMES[theme])}
                    scale={small}
                    label={`${look.skin} ${accessory}`}
                  />
                  <figcaption>{accessory}</figcaption>
                </figure>
              ))}
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2>Signals above the head</h2>
        <div className="lab-pieces">
          {(Object.keys(SIGNALS) as Signal[]).map((signal, i) => (
            <figure key={signal}>
              <Pixels
                width={STATION.width}
                height={STATION.height}
                pixels={stationPixels(combos[i % combos.length], poseOf('idle', tick, signal), THEMES[theme])}
                scale={small}
                label={signal}
              />
              <figcaption>{signal}</figcaption>
            </figure>
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
                pixels={pixelsOf(sprite, Math.floor(tick / 3), themeColors(THEMES[theme], screen))}
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
