# Openspace frontend

The pixel-art building and the panels around it.

## Stack

- [Vite](https://vite.dev) + React + TypeScript
- [PixiJS](https://pixijs.com) v8 for the building, rendered without
  antialiasing so pixel art stays crisp
- TanStack Query for server data, refreshed by SignalR messages
- Fonts bundled locally (Silkscreen, IBM Plex), so it works offline
- Markdown rendered with marked and sanitised with DOMPurify
- Sprites (planned): PNG sheets with Aseprite JSON

## Structure

```
src/
  agents/     Roles view and role form helpers (tested)
  activity/   Desk activity and energy: merge and lookup helpers for speech bubbles and batteries (tested)
  api/        Types, fetch client (problem details to readable errors), query hooks
  realtime/   SignalR connection: refetches what the server says changed
  layout/     Top bar with views, office sidebar
  building/   Floor plans and building shapes, walls and doors, room arranging and camera maths (pure, tested), their PixiJS rendering and the Arrange panel
  board/      Task board, new task dialog, task drawer, board rules (tested)
  rooms/      Rooms, room context and desks of an office
  art/        Pixel art: palettes, sprites as character matrices, station composition (tested)
  lab/        Sprite lab: open the app with ?lab to review every sprite and combination
  ui/         Toasts, dialog, status pill
```

## Views

| View | What it shows |
| --- | --- |
| Building | The office drawn in pixel art, in its mood (theme): scroll to zoom at the cursor, drag to move, double-click a room to fly into it, Fit to see it all. Every desk shows its agent in its real state (working, reading, waiting for you, queued, done, failed) with signals and speech bubbles; desks stand in rows of four. A battery beside each busy agent shows how much room is left in its conversation (green, yellow, red, and shorter as it empties): tired agents get heavy eyes and yawn, exhausted ones sink into their chair, and when the engine summarises the conversation the agent sips a coffee while the battery charges again. The Activity panel at the top right follows what happens live in this office, one of its rooms or every office (reads and searches on request); closed it shows the latest line, and a line opens its task on the board of its office. The theme is chosen per office, the lights can be switched off (remembered in this browser) |
| Board | Ask the supervisor to split a request into proposed tasks, then approve them all at once or one by one; filter by epic. Tasks in columns by status. Click a card for details, live activity, permission answers, the diff of the changes, merging, actions and notes; the Log tab shows the audit log (changes only, or everything) and downloads the Markdown report |
| Rooms | Set the office repository, open rooms, write their context and required procedure, add and rename desks and give them roles; the OpenCode engine and model sit under Advanced |
| Reports | Write a report of a day or of an epic in a chosen language, read it rendered, copy it or download it as Markdown |
| Agents | Define roles: description, instructions (with an AI draft to review before using it), preferred model and what the agent may do without asking |

While agents wait for a permission, "Needs you" cards pop up at the bottom right
of every view, one per desk, with Allow once and Refuse, and pop out once answered
(no motion when the system asks for reduced motion).

Confirmations appear as short toasts at the bottom; errors stay next to the form
that caused them, with the server's reason.

## Run

```bash
npm install
npm run dev     # http://localhost:5173, proxies /api and /hubs to the API on 5145
OPENSPACE_API=http://localhost:5245 npx vite --port 5273   # a second copy against another API
npm run build
npm run lint
npm test        # Vitest
```

## Status

Agents run end to end through OpenCode, with live bubbles, review and diff.
Sprites and the design pass come later.
