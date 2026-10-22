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
  viewer; its glow is the screen state.

## Characters

- Canvas of **24×32 px**, about 2.5 heads tall, seated: head and shoulders above
  the desk, hands resting on the desk top.
- Built from **layers**, drawn in this order:
  1. `body`: neck, torso and arms, coloured by the role's clothes;
  2. `head`: the face shape;
  3. `face`: eyes and mouth, one per emotion;
  4. `hair`: one of several styles, which may cover the forehead;
  5. `accessory`: glasses, headset… (optional);
  6. the desk, drawn by the room;
  7. `hands`: drawn **after** the desk, so they rest on it.
- Appearance is per desk: skin tone, hair style and colour, clothes colour (from
  the role by default) and an optional accessory.
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

| Agent state | Animation | Face |
| --- | --- | --- |
| No task | `idle` | `neutral` |
| Working | `typing` | `focused` |
| Reading, searching | `reading` | `focused` |
| Waiting for a person | `waiting` (hand raised) | `neutral` |
| Queued, no seat | `idle` | `asleep` |
| Work sent to review | `done` | `happy` |
| Failed | `stuck` | `worried` |

## Colour

Two kinds of palette:

- **Character palette**, the same in every office: outline, eyes, mouth, skin
  tones, hair colours and clothes colours.
- **Theme palette**, one per office mood: every colour of rooms, floors, walls,
  furniture and props is named by its meaning (`floor.light`, `wall.shadow`,
  `wood.mid`, `screen.on`…), and a theme gives each name its real colour. A new
  mood is a new table of colours, not new drawings. The first theme is
  **cozy evening** (warm wood, muted blue-grey walls, amber lights).

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
