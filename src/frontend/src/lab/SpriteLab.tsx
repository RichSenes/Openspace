import { useEffect, useMemo, useState } from 'react'
import { AGENT_LOOKS, poseOf, type AgentState } from '../art/agentStates'
import { toCanvas } from '../art/canvas'
import { inTheDark } from '../art/lighting'
import { CLOTH_COLORS, HAIR_COLORS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone } from '../art/palette'
import { sceneLights, scenePixels, type SceneItem } from '../art/scene'
import { ACCESSORIES, FACES, HAIR_STYLES, type Accessory, type Emotion, type HairStyle } from '../art/sheets/character'
import { PROPS, type PropName } from '../art/sheets/props'
import { SIGNALS, type Signal } from '../art/sheets/signals'
import { pixelsOf, type Pixel, type SpriteDef } from '../art/sprite'
import { STATION, stationLights, stationPixels, themeColors, type Appearance, type ScreenState } from '../art/station'
import { THEMES, type Theme, type ThemeName } from '../art/themes'
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
  { skin: 'tan', hair: 'auburn', hairStyle: 'bun', cloth: 'red', outfit: 'hoodie' },
  { skin: 'deep', hair: 'black', hairStyle: 'short', cloth: 'green' },
  { skin: 'medium', hair: 'blonde', hairStyle: 'long', cloth: 'mustard', costume: 'hooded' },
  { skin: 'fair', hair: 'grey', hairStyle: 'bun', cloth: 'grey', costume: 'skeleton' },
]

/** Where each theme's furniture stands in the sample room. */
const LAYOUTS: Record<ThemeName, { prop: PropName; x: number; y: number }[]> = {
  cozy: [
    { prop: 'rug', x: 70, y: 108 },
    { prop: 'bookshelf', x: 6, y: 6 },
    { prop: 'keySword', x: 36, y: 2 },
    { prop: 'floorLamp', x: 184, y: 4 },
    { prop: 'clubSofa', x: 4, y: 114 },
    { prop: 'dragonLamp', x: 192, y: 72 },
    { prop: 'sideTable', x: 172, y: 120 },
    { prop: 'whiteDog', x: 150, y: 123 },
    { prop: 'blackCat', x: 46, y: 104 },
    { prop: 'plant', x: 206, y: 112 },
  ],
  neonDistrict: [
    { prop: 'mechStatue', x: 8, y: 0 },
    { prop: 'serverRack', x: 182, y: 4 },
    { prop: 'vendingMachine', x: 202, y: 4 },
    { prop: 'redBike', x: 172, y: 112 },
    { prop: 'plant', x: 4, y: 112 },
    { prop: 'starWalker', x: 124, y: 126 },
  ],
  securityFirm: [
    { prop: 'whiteboard', x: 6, y: 6 },
    { prop: 'corpPoster', x: 92, y: 4 },
    { prop: 'waterCooler', x: 192, y: 10 },
    { prop: 'serverRack', x: 206, y: 4 },
    { prop: 'throne', x: 4, y: 110 },
    { prop: 'goldBars', x: 30, y: 122 },
    { prop: 'plant', x: 204, y: 112 },
    { prop: 'saveStar', x: 120, y: 124 },
  ],
  backRoomArcade: [
    { prop: 'arcadeCabinet', x: 4, y: 4 },
    { prop: 'arcadeCabinet', x: 22, y: 4 },
    { prop: 'serverRack', x: 182, y: 4 },
    { prop: 'vendingMachine', x: 202, y: 4 },
    { prop: 'itemPedestal', x: 196, y: 110 },
    { prop: 'sideTable', x: 110, y: 122 },
    { prop: 'whiteDog', x: 140, y: 125 },
  ],
}

/** Tile variants: the frame of a floor or wall tile depends on where it lies. */
const variant = (sprite: SpriteDef, c: number, r: number) => (c * 7 + r * 3) % sprite.frames.length

/**
 * The upper half of a wall: the tile's cap and panel without its skirting, so walls are
 * two tiles tall and leave room for what hangs on them.
 */
function upperWall(sprite: SpriteDef): SpriteDef {
  return { ...sprite, frames: sprite.frames.map((rows) => [...rows.slice(0, 10), ...rows.slice(2, 8)]) }
}

/** A sample room: two rows of desks among the theme's furniture, sorted by depth. */
function roomPixels(tick: number, name: ThemeName, dark: boolean): { width: number; height: number; pixels: Pixel[] } {
  const theme: Theme = THEMES[name]
  const cols = 14
  const rows = 7
  const colors = themeColors(theme.colors)
  const upper = upperWall(theme.wall)
  const floor: Pixel[] = []
  for (let c = 0; c < cols; c++) {
    floor.push(...pixelsOf(upper, variant(upper, c, 0), colors, c * 16, 0))
    floor.push(...pixelsOf(theme.wall, variant(theme.wall, c, 1), colors, c * 16, 16))
  }
  for (let r = 0; r < rows; r++)
    for (let c = 0; c < cols; c++) floor.push(...pixelsOf(theme.floor, variant(theme.floor, c, r), colors, c * 16, 32 + r * 16))

  const desk = (who: number, state: AgentState, x: number, y: number): SceneItem => {
    const pose = poseOf(state, tick + who * 7)
    return { x, y, height: STATION.height, pixels: stationPixels(PEOPLE[who], pose, theme), lights: stationLights(pose, theme) }
  }
  const items: SceneItem[] = [
    ...LAYOUTS[name].map(({ prop, x, y }): SceneItem => {
      const { sprite, lights, layer } = PROPS[prop]
      const resolved = (lights ?? []).map((l) => ({ ...l, color: colors(l.color) ?? '#ffffff' }))
      return { x, y, height: sprite.height, pixels: pixelsOf(sprite, Math.floor(tick / 2), colors), lights: resolved, layer }
    }),
    desk(0, 'working', 52, 20),
    desk(1, 'reading', 98, 20),
    desk(2, 'waiting', 144, 20),
    desk(3, 'done', 30, 64),
    desk(4, 'failed', 76, 64),
    desk(5, 'queued', 122, 64),
  ]
  const pixels = [...floor, ...scenePixels(items)]
  return { width: cols * 16, height: 32 + rows * 16, pixels: dark ? inTheDark(pixels, sceneLights(items), theme.colors.night) : pixels }
}

/**
 * Every sprite, animation and combination in one page, to review the art in one
 * place. Open the app with ?lab in the address.
 */
export function SpriteLab() {
  const tick = useTick()
  const [scale, setScale] = useState(4)
  const [themeName, setThemeName] = useState<ThemeName>('cozy')
  const [dark, setDark] = useState(false)
  const [hairStyle, setHairStyle] = useState<HairStyle>('short')
  const room = useMemo(() => roomPixels(tick, themeName, dark), [tick, themeName, dark])
  const theme = THEMES[themeName]
  const small = Math.max(2, scale - 1)

  const combos: Appearance[] = useMemo(() => {
    const skins = Object.keys(SKIN_TONES) as SkinTone[]
    const hairs = Object.keys(HAIR_COLORS) as HairColor[]
    const cloths = Object.keys(CLOTH_COLORS) as ClothColor[]
    return skins.map((skin, i) => ({ skin, hair: hairs[i % hairs.length], hairStyle, cloth: cloths[i % cloths.length] }))
  }, [hairStyle])

  const pieces: { sprite: SpriteDef; frame?: number; screen?: ScreenState }[] = [
    ...theme.floor.frames.map((_, frame) => ({ sprite: theme.floor, frame })),
    ...theme.wall.frames.map((_, frame) => ({ sprite: theme.wall, frame })),
    { sprite: theme.desk, screen: 'off' },
    { sprite: theme.desk, screen: 'on' },
    { sprite: theme.desk, screen: 'alert' },
  ]
  const looks: { label: string; look: Appearance }[] = [
    { label: 'shirt', look: { ...combos[0] } },
    { label: 'hoodie', look: { ...combos[1], cloth: 'black', outfit: 'hoodie' } },
    { label: 'coat', look: { ...combos[2], outfit: 'coat' } },
    { label: 'skeleton', look: { ...combos[3], costume: 'skeleton' } },
    { label: 'hooded', look: { ...combos[1], costume: 'hooded' } },
    { label: 'pointy hat', look: { ...combos[0], cloth: 'green', accessory: 'pointyHat' } },
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
          <select className="input" value={themeName} onChange={(e) => setThemeName(e.target.value as ThemeName)}>
            {(Object.keys(THEMES) as ThemeName[]).map((t) => (
              <option key={t} value={t}>
                {THEMES[t].label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Lights{' '}
          <select className="input" value={dark ? 'off' : 'on'} onChange={(e) => setDark(e.target.value === 'off')}>
            <option value="on">on</option>
            <option value="off">off</option>
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
                pixels={stationPixels(PEOPLE[i % PEOPLE.length], poseOf(state, tick + i * 3), theme)}
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
                    pixels={stationPixels(look, { emotion, gesture: 'rest', frame: 0, screen: 'on' }, theme)}
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
        <h2>Outfits and costumes</h2>
        <div className="lab-pieces">
          {looks.map(({ label, look }, i) => (
            <figure key={label}>
              <Station pixels={stationPixels(look, poseOf(i % 2 ? 'working' : 'idle', tick + i * 5), theme)} scale={small} label={label} />
              <figcaption>{label}</figcaption>
            </figure>
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
                    pixels={stationPixels({ ...look, accessory }, { emotion: 'neutral', gesture: 'rest', frame: 0, screen: 'on' }, theme)}
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
                pixels={stationPixels(combos[i % combos.length], poseOf('idle', tick, signal), theme)}
                scale={small}
                label={signal}
              />
              <figcaption>{signal}</figcaption>
            </figure>
          ))}
        </div>
      </section>

      <section>
        <h2>Furniture (hover for its line)</h2>
        <div className="lab-pieces">
          {(Object.keys(PROPS) as PropName[]).map((name) => {
            const { sprite, hint } = PROPS[name]
            return (
              <figure key={name} title={hint}>
                <Pixels
                  width={sprite.width}
                  height={sprite.height}
                  pixels={pixelsOf(sprite, Math.floor(tick / 2), themeColors(theme.colors))}
                  scale={scale}
                  label={hint}
                />
                <figcaption>{sprite.name}</figcaption>
              </figure>
            )
          })}
        </div>
      </section>

      <section>
        <h2>Pieces of the theme</h2>
        <div className="lab-pieces">
          {pieces.map(({ sprite, frame, screen }, i) => (
            <figure key={i}>
              <Pixels
                width={sprite.width}
                height={sprite.height}
                pixels={pixelsOf(sprite, frame ?? 0, themeColors(theme.colors, screen))}
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
