# Sprites

What the building needs to be drawn with real pixel art instead of placeholder
shapes, and the exact format the loader will expect. Until a sheet exists, the
building keeps drawing the current shapes, so sprites can arrive one at a time.

## General rules

- PNG with transparency, no anti-aliasing, no blur: every pixel is a hard pixel.
- One shared, limited palette (about 24–32 colours) for everything, so sheets
  made at different times still look like one world.
- The world is measured in 16×16 px tiles. Sprites are drawn at 1× and scaled by
  whole numbers on screen.
- View: top-down three-quarter ("RPG office"), light from the top left.
- Each sheet comes with a small JSON next to it (same name, `.json`) listing frame
  size and animations; Aseprite's "Export sprite sheet" JSON is accepted as is.

## 1. Agent (the person at a desk)

`sprites/agent.png`: frames of 32×32 px, one row per animation, read left to right.

| Row | Animation | Frames | When it shows |
| --- | --- | --- | --- |
| 1 | `idle` | 2 | Seated, nothing to do |
| 2 | `typing` | 4 | Editing files, running commands |
| 3 | `reading` | 2 | Reading, searching, thinking |
| 4 | `waiting` | 2 | Hand raised: needs a person's answer |
| 5 | `sleeping` | 2 | No task, or queued without a seat ("Zz") |
| 6 | `done` | 4 | Short celebration when work goes to review |
| 7 | `stuck` | 2 | Failed: head in hands, or a small storm cloud |

The agent is seen from behind-ish, facing its monitor, sitting on a chair; the
desk is a separate sprite drawn in front, so the agent must leave room for it in
the lower third of the frame. Variants (skins) are separate files with the same
layout: `agent-<name>.png`.

## 2. Desk

`sprites/desk.png`: frames of 48×32 px.

| Frame | Meaning |
| --- | --- |
| 1 | Monitor off |
| 2–3 | Monitor on (two-frame flicker or scrolling code) |
| 4 | Monitor showing an alert (waiting for you) |

## 3. Room tiles

`sprites/tiles.png`: 16×16 px tiles in a grid, in this order:

1. floor A, 2. floor B (for the checker), 3. wall top, 4. wall front,
5. wall left, 6. wall right, 7–10. the four outer corners, 11. door (open),
12. window on a wall front.

## 4. Props (optional)

`sprites/props.png`: 16×16 or 16×32 px, any of: plant, coffee machine, water
cooler, whiteboard, bookshelf, server rack (for server rooms), filing cabinet.
Rooms will use them as decoration in free corners.
