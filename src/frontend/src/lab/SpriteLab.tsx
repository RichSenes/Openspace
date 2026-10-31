import { useEffect, useMemo, useState } from 'react'
import { AGENT_LOOKS, poseOf, type AgentState } from '../art/agentStates'
import { toCanvas } from '../art/canvas'
import { CLOTH_COLORS, HAIR_COLORS, SKIN_TONES, type ClothColor, type HairColor, type SkinTone } from '../art/palette'
import { deskItem, internItems, propItem, roomPixels, roomShell, TILE, WALL_HEIGHT } from '../art/roomScene'
import { scenePixels } from '../art/scene'
import type { OnStage } from '../activity/interns'
import type { SceneItem } from '../art/scene'
import { ACCESSORIES, FACES, HAIR_STYLES, type Accessory, type Emotion, type HairStyle } from '../art/sheets/character'
import { FURNITURE } from '../art/fanPack'
import type { PropName } from '../art/sheets/props'
import { SIGNALS, type Signal } from '../art/sheets/signals'
import { pixelsOf, type Pixel, type SpriteDef } from '../art/sprite'
import { STATION, stationPixels, themeColors, type Appearance, type ScreenState } from '../art/station'
import { THEMES, type Theme, type ThemeName } from '../art/themes'
import { GLASS, skyPixels, windowPixels, WINDOW_SIZE, type DayPhase } from '../art/windows'
import type { Sky } from '../api/types'
import { walkerPixels, WALKER } from '../art/walker'
import { WalkStage, type DeskDoing } from '../activity/walks'
import { walkerItems } from '../art/chat'
import { placesOf } from '../building/stations'
import type { BuildingLayout, RoomBox } from '../building/layout'
import './lab.css'

const FPS = 5

/** Rows at the top of a station kept free for signals; cropped where no signal is shown. */
const SIGNAL_ROOM = 11

const EMOTIONS = Object.keys(FACES).filter((e) => e !== 'blink') as Emotion[]
const STATES = Object.keys(AGENT_LOOKS) as AgentState[]

/** Agents at every level of energy, plus a coffee break that starts again every eight seconds. */
const ENERGY_SAMPLES: { label: string; state: AgentState; left: number; coffee?: true }[] = [
  { label: 'coffee break', state: 'working', left: 1, coffee: true },
  { label: 'fresh', state: 'working', left: 0.95 },
  { label: 'steady', state: 'working', left: 0.45 },
  { label: 'tired', state: 'working', left: 0.2 },
  { label: 'exhausted', state: 'working', left: 0.05 },
  { label: 'tired, reading', state: 'reading', left: 0.2 },
  { label: 'tired, asks you', state: 'waiting', left: 0.2 },
  { label: 'done', state: 'done', left: 0.55 },
]

const SKIES: Sky[] = ['Clear', 'Cloudy', 'Fog', 'Drizzle', 'Rain', 'Snow', 'Storm']
const PHASES: DayPhase[] = ['day', 'dusk', 'night']

/** One window per time of day, side by side, in the same weather. */
function weatherStrip(weather: Sky, tick: number, theme: Theme): Pixel[] {
  const colors = themeColors(theme.colors)
  return PHASES.flatMap((phase, i) =>
    windowPixels(skyPixels(i * 40, GLASS.width, GLASS.height, tick, phase, weather), colors).map((p) => ({ ...p, x: p.x + i * (WINDOW_SIZE.width + 2) })),
  )
}

/** A room where agents get up: one reads and runs commands in turn, two take breaks often. */
const WALK_ROOM: RoomBox = {
  id: 'walks',
  name: 'Walks',
  x: 0,
  y: 0,
  width: 14,
  height: 9,
  desks: [
    { id: 'w1', name: 'Reader', x: 40, y: 56 },
    { id: 'w2', name: 'Idle', x: 104, y: 56 },
    { id: 'w3', name: 'Done', x: 152, y: 92 },
  ],
  props: [
    { name: 'bookshelf', x: 8, y: 30 },
    { name: 'filingCabinet', x: 46, y: 22 },
    { name: 'printer', x: 6, y: 100 },
    { name: 'serverRack', x: 186, y: 26 },
    { name: 'whiteboard', x: 140, y: 26 },
    { name: 'coffeeMachine', x: 196, y: 104 },
  ],
  door: { wall: 'Front', offset: 5 },
  windows: [],
}
const WALK_LAYOUT: BuildingLayout = { rooms: [WALK_ROOM], floor: { x: 0, y: 0, width: 14, height: 9 }, cuts: [], doors: [], hall: [], planned: true, width: 14, height: 9 }
const WALK_PLACES = placesOf(WALK_LAYOUT)
const WALK_STEPS = ['Reading a.ts', 'Running the tests', 'Searching the code', 'Reading b.ts', 'Building the project', 'Reading c.ts', 'Planning the work', 'Reading d.ts']

function walkScene(stage: WalkStage, tick: number, theme: Theme): Pixel[] {
  const now = (tick / FPS) * 1000
  const step = WALK_STEPS[Math.floor(now / 7000) % WALK_STEPS.length]
  const doing = new Map<string, DeskDoing>([
    ['w1', { state: step.startsWith('Reading') || step.startsWith('Searching') ? 'reading' : 'working', doing: `${step} ${Math.floor(now / 7000)}` }],
    ['w2', { state: 'idle' }],
    ['w3', { state: 'done' }],
  ])
  stage.update(WALK_PLACES, doing, now)
  const room = WALK_ROOM
  const size = { width: room.width * TILE, height: room.height * TILE }
  const items: SceneItem[] = [
    ...room.props.map((p) => propItem(p.name, p.x, p.y, tick, theme, size)),
    ...room.desks.flatMap((d, i) => {
      const away = stage.at(d.id, now)
      const state = doing.get(d.id)!.state
      const desk = deskItem(PEOPLE[i], state, d.x, d.y, tick + i * 5, theme, undefined, undefined, Boolean(away))
      return away ? [desk, ...walkerItems(PEOPLE[i], away, 0, 0)] : [desk]
    }),
  ]
  return roomPixels(theme, roomShell(theme, room.width, room.height - WALL_HEIGHT / TILE), items, false)
}

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

const INTERN_SCENE = { width: 72, height: 52, deskX: 20 }
const INTERN_LABELS = ['one intern', 'two interns', 'arriving and leaving (3 s loop)']
const SAMPLE_INTERN = { id: 'a', what: 'Find the login form', since: '' }

/** A desk with its interns: one, two, or one coming and going in a puff every three seconds. */
function internScene(look: Appearance, sample: number, tick: number, theme: Theme): Pixel[] {
  const t = (tick % (3 * FPS)) / FPS
  const frame = (since: number) => Math.min(5, Math.floor(since * 6))
  const looping: OnStage =
    t < 1 ? { intern: SAMPLE_INTERN, spot: 0, visible: frame(t) >= 3, puff: frame(t) }
    : t < 2 ? { intern: SAMPLE_INTERN, spot: 0, visible: true }
    : { intern: SAMPLE_INTERN, spot: 0, visible: frame(t - 2) < 3, puff: frame(t - 2) }
  const stage: OnStage[] =
    sample === 0 ? [{ intern: SAMPLE_INTERN, spot: 0, visible: true }]
    : sample === 1 ? [{ intern: SAMPLE_INTERN, spot: 0, visible: true }, { intern: { ...SAMPLE_INTERN, id: 'zz' }, spot: 1, visible: true }]
    : [looping]
  const x = INTERN_SCENE.deskX
  return scenePixels([deskItem(look, 'working', x, 0, tick, theme), ...internItems(look, stage, x, 0, tick)])
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
  brightStudio: [
    { prop: 'rug', x: 70, y: 108 },
    { prop: 'bookshelf', x: 6, y: 6 },
    { prop: 'floorLamp', x: 184, y: 4 },
    { prop: 'sideTable', x: 172, y: 120 },
    { prop: 'whiteDog', x: 150, y: 123 },
    { prop: 'plant', x: 206, y: 112 },
    { prop: 'plant', x: 4, y: 112 },
  ],
  darkFocus: [
    { prop: 'serverRack', x: 182, y: 4 },
    { prop: 'serverRack', x: 200, y: 4 },
    { prop: 'vendingMachine', x: 6, y: 4 },
    { prop: 'plant', x: 204, y: 112 },
  ],
  townHall: [
    { prop: 'whiteboard', x: 6, y: 6 },
    { prop: 'waterCooler', x: 192, y: 10 },
    { prop: 'plant', x: 204, y: 112 },
  ],
}

/** A sample room: two rows of desks among the theme's furniture, sorted by depth. */
function roomPixelsAt(tick: number, name: ThemeName, dark: boolean): { width: number; height: number; pixels: Pixel[] } {
  const theme: Theme = THEMES[name]
  const cols = 14
  const rows = 7
  const size = { width: cols * TILE, height: WALL_HEIGHT + rows * TILE }
  const desk = (who: number, state: AgentState, x: number, y: number) => deskItem(PEOPLE[who], state, x, y, tick + who * 7, theme)
  const items: SceneItem[] = [
    ...LAYOUTS[name].map(({ prop, x, y }) => propItem(prop, x, y, tick, theme, size)),
    desk(0, 'working', 52, 20),
    desk(1, 'reading', 98, 20),
    desk(2, 'waiting', 144, 20),
    desk(3, 'done', 30, 64),
    desk(4, 'failed', 76, 64),
    desk(5, 'queued', 122, 64),
  ]
  return { ...size, pixels: roomPixels(theme, roomShell(theme, cols, rows), items, dark || Boolean(theme.dark)) }
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
  const room = useMemo(() => roomPixelsAt(tick, themeName, dark), [tick, themeName, dark])
  const [walks] = useState(() => new WalkStage(20_000))
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
    { label: 'karakuri (routines)', look: { ...combos[0], costume: 'karakuri' } },
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
        <h2>Energy</h2>
        <div className="lab-pieces">
          {ENERGY_SAMPLES.map((sample, i) => (
            <figure key={sample.label}>
              <Pixels
                width={STATION.width}
                height={STATION.height}
                pixels={stationPixels(
                  PEOPLE[i % PEOPLE.length],
                  poseOf(sample.state, tick + i * 5, undefined, sample.coffee ? { left: 1, sinceRefresh: (tick / FPS) % 8 } : { left: sample.left }),
                  theme,
                )}
                scale={small}
                label={sample.label}
              />
              <figcaption>{sample.label}</figcaption>
            </figure>
          ))}
        </div>
      </section>

      <section>
        <h2>Interns</h2>
        <div className="lab-pieces">
          {[0, 1, 2].map((i) => (
            <figure key={i}>
              <Pixels width={INTERN_SCENE.width} height={INTERN_SCENE.height} pixels={internScene(PEOPLE[i], i, tick, theme)} scale={small} label={INTERN_LABELS[i]} />
              <figcaption>{INTERN_LABELS[i]}</figcaption>
            </figure>
          ))}
        </div>
      </section>

      <section>
        <h2>On their feet (front, walking, back, on a break, walking right and left)</h2>
        <div className="lab-pieces">
          {[...PEOPLE, { skin: 'fair', hair: 'blonde', hairStyle: 'long', cloth: 'green', outfit: 'coat', accessory: 'pointyHat' } as Appearance].map((look, i) => (
            <figure key={i}>
              <Pixels
                width={WALKER.width * 6}
                height={WALKER.height}
                pixels={[
                  ...walkerPixels(look, { facing: 'right', step: tick }).map((p) => ({ ...p, x: p.x + WALKER.width * 4 })),
                  ...walkerPixels(look, { facing: 'left', step: tick + 1 }).map((p) => ({ ...p, x: p.x + WALKER.width * 5 })),
                  ...walkerPixels(look, { facing: 'front', step: 0 }),
                  ...walkerPixels(look, { facing: 'front', step: tick }).map((p) => ({ ...p, x: p.x + WALKER.width })),
                  ...walkerPixels(look, { facing: 'back', step: tick }).map((p) => ({ ...p, x: p.x + WALKER.width * 2 })),
                  ...walkerPixels(look, { facing: 'front', step: 0, sip: true, emotion: 'happy' }).map((p) => ({ ...p, x: p.x + WALKER.width * 3 })),
                ]}
                scale={small}
                label={`person ${i + 1}`}
              />
            </figure>
          ))}
        </div>
      </section>

      <section>
        <h2>Walks (stations, breaks and chats)</h2>
        <Pixels width={WALK_ROOM.width * TILE} height={WALK_ROOM.height * TILE} pixels={walkScene(walks, tick, theme)} scale={small} label="Walks" />
      </section>

      <section>
        <h2>Weather (day, dusk, night)</h2>
        <div className="lab-pieces">
          {SKIES.map((weather) => (
            <figure key={weather}>
              <Pixels width={WINDOW_SIZE.width * 3 + 4} height={WINDOW_SIZE.height} pixels={weatherStrip(weather, tick, theme)} scale={small} label={weather} />
              <figcaption>{weather}</figcaption>
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
          {(Object.keys(FURNITURE) as PropName[]).map((name) => {
            const { sprite, hint } = FURNITURE[name]
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
