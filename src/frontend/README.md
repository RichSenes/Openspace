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
| Building | The office as a pixel floor plan. Rooms are laid out to get the biggest crisp zoom; a desk's screen lights up while its task runs |
| Board | Tasks in columns by status. Click a card for details, actions and notes |
| Rooms | Open rooms, write their context, add desks bound to OpenCode agents |

Confirmations appear as short toasts at the bottom; errors stay next to the form
that caused them, with the server's reason.

## Run

```bash
npm install
npm run dev     # http://localhost:5173, proxies /api and /hubs to the API on 5145
npm run build
npm run lint
npm test        # Vitest
```

## Status

Offices, rooms, desks and tasks can be managed end to end. Agents and sprites
are next.
