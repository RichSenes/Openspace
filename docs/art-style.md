# Art style

The rules every Openspace sprite follows. They are decided once so that art made
at different times still looks like one world. The sprite format (rows of
characters, one per pixel) and where sprites live are described at the end.

## Grid and scale

- The world is measured in **16×16 px tiles**.
- Sprites are drawn at 1× and shown scaled by whole numbers (3× by default), so
  every pixel stays a hard square. No anti-aliasing, no blur, no sub-pixel moves.

## View and light

- **Top-down, three-quarter view**: floors seen from above, walls with some height,
  objects showing their top and their front.
- **Light from the top left.** Surfaces facing up and left are lighter; right and
  bottom edges take the shadow.
- Agents sit **behind** their desk, **facing the viewer**, so their expressions are
  visible. The monitor stands on the left of the desk, its back towards the
  viewer; its glow is the screen state. A lit screen has a halo around it and
  spills its light on the desk top; an alert screen flashes (two frames lit, two
  dark); a screen that is off has neither.

## Characters

- Canvas of **24×32 px**, about 2.5 heads tall, seated: head and shoulders above
  the desk, hands resting on the desk top.
- Built from **layers**, drawn in this order:
  1. `body`: neck, torso and arms, coloured by the role's clothes;
  2. `head`: the face shape;
  3. `face`: eyes and mouth, one per emotion;
  4. `hair`: one of several styles, which may cover the forehead;
  5. `accessory`: `glasses` or `headset` (optional);
  6. `arms` of a gesture that leaves the desk (raised hand, hands on the cheeks…),
     drawn **before** the desk so the monitor stays in front of them;
  7. the desk, drawn by the room;
  8. `hands`: drawn **after** the desk, so they rest on it.
- Appearance is per desk: skin tone, hair style and colour, clothes colour (from
  the role by default), an optional outfit (`hoodie`, long `coat`) and an optional
  accessory (`glasses`, `headset`, `pointyHat`).
- Special agents can wear a **costume**, which replaces head, hair and clothes;
  its eyes close to blink and sleep: `skeleton` (round skull with a fixed grin,
  blue fur-lined hoodie) and `hooded` (long black coat whose hood hides the face
  completely, two yellow eyes glowing in the dark, black gloves).
- Emotions: `neutral`, `focused`, `happy`, `worried`, `asleep`.

## Outline and shading

- Outlines are **dark and tinted** (`outline`), never pure black.
- Each material has **two or three tones**: light, mid, shadow. Characters keep
  shading minimal: one shadow tone on the right side and under the chin.
- **No dithering**: flat areas of colour read better at small sizes.

## Animation

- 2 to 4 frames per animation, at about 5 frames per second.
- Movement is small: one pixel of breathing, hands moving on the keyboard.
- Idle characters blink every few seconds (a separate one-frame face).

| Agent state | Gesture | Face | Screen | Signal |
| --- | --- | --- | --- | --- |
| `idle`: no task | `rest` | `neutral` | off | |
| `working` | `typing` | `focused` | on | |
| `reading`: reading, searching | `think` (hand on the chin) | `focused` | on | |
| `waiting` for a person | `wave` (hand raised) | `neutral` | alert | `question` |
| `queued`, no seat | `rest` | `asleep` | off | `zz` |
| `done`: sent to review | `cheer` (both fists raised) | `happy` | on | `check` |
| `failed` | `stuck` (hands on the cheeks) | `worried` | off | `storm` |

While working or reading, the agent's **energy** changes its look. Energy is the room
left in its conversation, shown by a battery left of the agent (`art/energy.ts`): the
charge gets shorter as the conversation fills, so it reads without colours, and it is
green above 60% left, yellow down to 30%, red below.

| Energy left | Face | Also |
| --- | --- | --- |
| above 30% | the state's own | |
| 10% to 30%: `tired` | `tired` (heavy lids, shadows under the eyes) | `yawn` every eight seconds |
| below 10%: `exhausted` | `tired` | sinks two pixels into the chair, types at half speed, yawns every five seconds |
| just got it back | `blink` with the `sip` gesture (a red mug at the mouth) for three seconds while the battery charges, then `happy` for two | |

**Interns** are helpers an agent called in: 12×22, standing beside the desk (right,
then left; two at most), in their agent's colours with a skin and hair of their own, a
visitor badge on a lanyard and a clipboard (`art/interns.ts`). They come and go in a
six-frame puff of smoke with sparks (`PUFF`, one second): a small ball, billows taller
than the intern in four greys lit from the top left, then wisps drifting apart. The intern
shows up or goes away behind the thickest frame. With reduced motion they
simply appear and disappear.

Sweat drops and the hand on the chin are taken (`worried` and `reading`), so they are
not used for tiredness. States that concern you (waiting, done, failed) keep their own
look and only show the battery.

The table lives in `src/frontend/src/art/agentStates.ts`. Momentary signals, such as
`bulb` for a finding or `exclaim` for a failed command, replace the state's own
signal for a few seconds.

## Signals above the head

A 9×10 icon above the head says at a glance what happened. Each one differs from
the others by **shape**, not only by colour, so it reads for colour-blind people
too.

| Signal | Shape | Meaning |
| --- | --- | --- |
| `bulb` | light bulb | The agent found something or left a note |
| `exclaim` | exclamation mark | Something unexpected: a command failed, a conflict |
| `question` | question mark | Waiting for a person (a permission, an answer) |
| `zz` | Z in a bubble | Queued, waiting for a seat |
| `check` | tick | Work sent to review |
| `storm` | cloud with lightning | The task failed |

## Furniture and depth

Props stand anywhere in a room, each with a line of flavour shown on hover
(`src/frontend/src/art/sheets/props.ts`). A sprite's bottom row is where it
touches the floor, its **base line**. A room draws rugs first, then what hangs on
the walls, then desks and props sorted by base line, so whatever stands lower on
the screen is closer to the viewer and covers what is behind it. Desks can
therefore stand in several rows, not only side by side.

Walls are two tiles tall: the upper tile repeats the panel without the skirting,
so masks and swords have room to hang.

An office is one building. Seen from above, walls between spaces are a thin cap
(4 px, 6 px for the outer walls) in `wall.top` with an `outline` edge; the front
wall of a room also shows a few pixels of its face on the corridor side, and
stops at the door, framed by two jambs in `trim.light`. The common floor uses the
hall tile (`HALL_FLOOR` in `sheets/rooms.ts`): big square tiles in the wall
colours, so corridors never read as part of a room. Each theme lists its corridor
furniture in `hall`.

Windows (`src/frontend/src/art/windows.ts`) are 24 by 22: a frame in `trim.light`,
a cross that splits four panes, a sill, and a two-pixel glint in each pane. On an
outer back wall they show the sky for the hour on the viewer's clock (day, dusk,
night) with bands dithered like pixel art, a far skyline, drifting clouds by day
and twinkling stars and lit windows by night; by day they light a dark room. With
a town set in Settings they show its weather too (`Sky`): cloudy greys the bands and
sends two layers of heavier clouds across, the near one faster; fog veils everything,
thicker low down; drizzle, rain and storms fall in drops slanting left, more and
longer each time, in front of the skyline; snow sways down slowly; a storm flashes
now and then, lighting the room. Stars shine only on a clear night, and a grey sky
lets in less, greyer light. On an
inner wall they show what is painted just behind the wall. Either way the view is
glazed, a little paler and bluer, so a window never reads as a hole. A door in a
back wall is a doorway with posts and a lintel that shows the same view.

### Homages

Some furniture and outfits are affectionate homages to games, films and series
the team loves: a rare dragon lamp, a stack of gold bars, a throne, a leather
club sofa, a key-shaped sword with its charm, a long red motorbike, a purple giant
robot statue, a heart on a pedestal, a white dog, a twinkling save star, a proud
little star, a black cat waiting for something to happen, a corporate poster, a
hooded black coat with glowing eyes, a skeleton in a blue hoodie, a soft green
pointy hat. They are drawn by us, recognisable on purpose, and **never named after their source**: in code, docs
and hints they have descriptive names and their lines quote or wink without
naming anything.

## Colour

Two kinds of palette:

- **Character palette**, the same in every office: outline, eyes, mouth, skin
  tones, hair colours and clothes colours.
- **Theme palette**, one per office mood: every colour of rooms, floors, walls and
  desks is named by its meaning (`floor.light`, `wall.shadow`, `wood.mid`,
  `screen.on`…), and a theme gives each name its real colour.
- **Object colours**, the same everywhere: furniture keeps its own colours (a
  green dragon is green in every office).

A theme is more than colours: it brings its own floor, wall and desk model
(`src/frontend/src/art/themes.ts`) and the furniture that suits it.

| Theme | Floor | Walls | Desk | Furniture |
| --- | --- | --- | --- | --- |
| Cozy evening (default) | wooden planks | blue-grey panels | wood, monitor | rug, bookshelf, lamps, plants |
| Neon district | metal plates with cyan lights | hexagon panels, hazard stripes, neon line | glass and metal, holographic screen | bike, robot statue, vending machine, servers |
| Security firm | carpet tiles | white, company stripe, windows with blinds | laminate, phone, stripe | whiteboard, water cooler, fan, plants |
| Back-room arcade | confetti carpet | bare bricks, graffiti, neon line | folding table, CRT, soda can | arcade cabinets, mask, vending machine |

Floor and wall frames are tile variants, picked by position, so a room is not a
grid of identical tiles.

### Fan pack (local only)

Faithful one-to-one copies of artwork we do not own (a sprite from a game, say) never
go into this repository. They live in `src/frontend/src/art/fanpack/`, which git
ignores, and replace the drawing of an existing prop on that machine only; in the
repository the prop keeps its own homage drawing.

`tools/pixelgrid.py` makes them from a picture of the pixel art, even blown up or
saved as JPEG: it finds the size of one pixel, samples the grid back, makes the
background transparent (keeping background-coloured eyes inside the figure) and
writes `fanpack/<prop>.ts`. Animated GIFs become animation frames.

```bash
python3 tools/pixelgrid.py dragonLamp ~/Downloads/dragon.jpg --height 44 --light 30,8,30,flame.mid
python3 tools/pixelgrid.py blackCat ~/Downloads/cat.gif --height 12
python3 tools/pixelgrid.py keySword ~/Downloads/key.png --largest --hole 28,59 --height 44
```

Copies are often bigger than our scale: `--height` brings them down to match the
agents, keeping outlines crisp. `--largest` drops signatures and captions, `--hole`
empties a ring's inside, `--cell`/`--offset` set the grid by hand when a picture is
stretched, `--outline` adds a black line for art drawn on black. A fan module can
also redraw a costume (`costumes.skeleton`, with `open`, `closed` and `body`).

The tool has its own tests: `python3 -m unittest discover tools`.

### Lights off

Any theme can be shown with the lights off. Everything sinks towards the theme's
`night` colour, except what glows by itself (screens, neon, lamps, LEDs, flames,
signals: the `EMISSIVE` names), and every light brings back its surroundings in
hard-edged rings tinted by its colour: lit monitors, lamps, arcade screens,
vending machines. Signals always stay readable.

Faces must read on every skin tone: eyes and mouth differ from the light skin
tone by a **contrast of at least 3:1** (WCAG luminance, not hue), which tests
enforce. That is why the mouth colour depends on the skin: dark on light skins,
light on the deepest one.

The exact colours live in `src/frontend/src/art/palette.ts`, which is the source
of truth.

## Sprite format

Sprites are written in code as rows of characters, one character per pixel, with
a legend that maps characters to palette names. `.` is always transparent. For
example, a 4×3 sprite:

```ts
{
  name: 'example',
  width: 4,
  height: 3,
  legend: { o: 'outline', w: 'wood.mid' },
  frames: [['.oo.', 'owwo', '.oo.']],
}
```

Tests check every sprite: rows of the right width, the right number of rows,
every character in the legend, every legend name in every palette. The sprite
lab (a page of the app, available while developing) shows every sprite,
animation and combination at several zoom levels.

## Minimap

The building's little map (`src/frontend/src/building/minimap.ts`) is a sketch, not a
copy: two map pixels per tile, enlarged on screen by whole steps only. The corridors are
the office's wall grey with the building's edge in its outline colour, each room a block in
its own mood's floor colour with its back wall along the top, and each agent a mark three
pixels across with a dark rim, so its state reads from the shape as well as the colour: a
diamond at work (green, pulsing) or reading (blue), a ring in the queue, a dot when idle, a
square when done, an exclamation mark that blinks when it needs you, a red cross when it
failed. A light frame shows what the screen sees.
