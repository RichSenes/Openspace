# Openspace frontend

The pixel-art building and the panels around it.

## Stack

- [Vite](https://vite.dev) + React + TypeScript
- [PixiJS](https://pixijs.com) v8 for the building, rendered without
  antialiasing so pixel art stays crisp
- TanStack Query for server data, refreshed by SignalR messages
- Fonts bundled locally (Silkscreen, IBM Plex), so it works offline
- Sprites (planned): PNG sheets with Aseprite JSON

## Structure

```
src/
  agents/     Roles view and role form helpers (tested)
  activity/   Desk activity: merge and lookup helpers for speech bubbles (tested)
  api/        Types, fetch client (problem details to readable errors), query hooks
  realtime/   SignalR connection: refetches what the server says changed
  layout/     Top bar with views, office sidebar
  building/   Floor plan layout (pure, tested) and its PixiJS rendering
  board/      Task board, new task dialog, task drawer, board rules (tested)
  rooms/      Rooms, room context and desks of an office
  ui/         Toasts, dialog, status pill
```

## Views

| View | What it shows |
| --- | --- |
| Building | The office as a pixel floor plan. Rooms are laid out to get the biggest crisp zoom; a desk's screen lights up while its agent works, and a speech bubble says what it is doing |
| Board | Ask the supervisor to split a request into proposed tasks, then approve them all at once or one by one; filter by epic. Tasks in columns by status. Click a card for details, live activity, permission answers, the diff of the changes, merging, actions and notes; the Log tab shows the audit log (changes only, or everything) and downloads the Markdown report |
| Rooms | Set the office repository, open rooms, write their context, add and rename desks and give them roles; the OpenCode engine and model sit under Advanced |
| Agents | Define roles: description, instructions (with an AI draft to review before using it), preferred model and what the agent may do without asking |

While any agent waits for a permission, a "Needs you" strip sits on top of every
view with Allow once and Refuse.

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
