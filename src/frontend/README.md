# Openspace frontend

The page: the pixel-art building and the panels around it. It talks to the API
over HTTP and keeps itself up to date through the SignalR hub.

## Stack

- [Vite](https://vite.dev), React 19 and TypeScript (`erasableSyntaxOnly`: no
  enums, no parameter properties)
- [PixiJS](https://pixijs.com) 8 for the building, drawn without antialiasing so
  pixel art stays crisp
- TanStack Query for server data; SignalR messages write into its cache or say
  what to fetch again
- Fonts bundled (Silkscreen, IBM Plex), so it works offline
- Markdown rendered with marked and sanitised with DOMPurify
- Sprites are TypeScript character matrices, one string per row of pixels: see
  [docs/sprites.md](../../docs/sprites.md) and
  [docs/art-style.md](../../docs/art-style.md)
- Vitest for the logic, oxlint for the rest

## Structure

```
src/
  api/        Types, fetch client (problem details to readable errors), query hooks
  realtime/   The SignalR connection: merges pushed data, refetches what changed
  layout/     Top bar, the strip of account and graphics card notices, office sidebar
  building/   The building: layout and floor plans, walls and doors, camera, minimap,
              stations, guests, the Arrange panel, agent cards and the activity feed
  art/        Pixel art: palette, themes, sprites, rooms and scenes, lighting, weather
              at the windows, walkers in four directions
  activity/   What desks are doing: bubbles, energy, interns, walks and chats,
              "needs you"
  board/      Task board, task drawer, new task, the supervisor, the Merge desk
  rooms/      Rooms of an office: repository, context, procedure, checks, desks,
              private folders, reserve models
  agents/     Roles, their instructions and permissions
  routines/   Routines, their runs, proposals, the paper's kanji
  reports/    Reports and changelogs
  security/   Security audits: runs, reports, findings, fixes
  engines/    OpenCode and Claude Code, usage limits, the graphics card
  settings/   Settings, weather, version, backups, Google accounts
  sounds/     Built-in sounds, your own files, the rules of when they play
  release/    The new version banner
  weather/    Words for the weather
  lab/        The sprite lab: open the page with ?lab
  ui/         Toasts, dialogs, status pills
```

Pure logic lives in `.ts` files with a `.test.ts` next to them; components stay
thin.

## Views

**Building.** The office drawn in pixel art, in its theme, with a mood per room.
Scroll to zoom, drag to move, double-click a room to fly into it, Fit to see it
all. Every desk shows its agent in its real state (working, reading, waiting for
you, queued, done, failed) with signals and speech bubbles; a battery beside busy
agents shows how full their conversation is, and interns appear in a puff of smoke
when an agent calls in helpers. Agents walk to the furniture of their room that
fits what they do (shelves to read, server racks for commands, whiteboards to
plan, the coffee machine on a break), hurry back when they need you, and stop for
a chat when they meet. Claude Code sessions started outside Openspace sit in the
guest room. Windows show the real weather of your town. The minimap in the corner
sketches the whole building; the Activity panel follows the office live.
**Arrange** lets you draw rooms and the building by hand: sizes, desks, furniture,
doors, windows and the building's shape. Nobody walks while you arrange or when
the system asks for reduced motion.

**Board.** Tasks in columns by status, searchable, filtered by epic. Ask the
supervisor to split a request into tasks and approve them. A queued task says why
it waits. Open a task for its details, live activity, permission answers, diff,
checks to try by hand, notes and actions; the Log tab shows the audit log and
downloads its Markdown report. The Merge desk merges accepted work, brings one
branch into another and pushes.

**Rooms.** The office's repository, rooms and their context, procedure and check,
desks with their name, role, engine and model, private folders, reserve models,
keeping the project on this computer.

**Routines.** Each routine with its schedule, settings, runs and log, what it
proposes (yes or no), and run now.

**Reports.** Reports of a day or of an epic, and changelogs, in a chosen language:
read them, copy them, download them.

**Security.** The office's security audits: add a Security desk, start an audit (quick,
standard or deep), follow its runs, read the report and the findings (confirmed worst
first, those to verify, the rejected), and propose a fix task for a confirmed one.

**Agents.** Roles: description, instructions (with an AI draft to review),
preferred model, what the agent may do without asking, and which installed skills it
may load (all of them, or the ones ticked).

**Settings.** Permissions, agents at work at once, who plans and reviews (Claude
Code, or OpenCode models chosen for plans and for reviews), the Claude account's limits, sounds, the weather's town, the version, backups and
Google accounts.

Across every view:

- Under the views, a strip shows what accounts and the graphics card are up to (a
  provider over its limits, who uses the local model, the Claude account), only
  when there is something to say; it wraps on narrow windows, where the sound and
  connection words become icons.
- When a newer release is out, a banner says so with what changed, a link and
  "Remind me tomorrow" (remembered in this browser for that version). Openspace
  never updates itself.
- "Needs you" in the top bar lists the agents waiting for your answer.
- Confirmations are short toasts at the bottom; errors stay next to the form that
  caused them, with the server's reason.

## Run

```bash
npm ci
npm run dev       # http://localhost:5173; /api and /hubs go to the API on 5145
npm run build     # type-checks, then builds
npm run lint
npm test          # Vitest
```

To run a second copy against another API (never leave `OPENSPACE_API` out, or it
talks to the one on 5145):

```bash
OPENSPACE_API=http://127.0.0.1:5245 npx vite --port 5273 --strictPort
```

## Fan pack

`src/art/fanpack/` may hold local redrawings of some props and costumes, copied
from pictures with `tools/pixelgrid.py`. Git ignores the folder: they are never
committed, never in screenshots, and the tests always check the repository's own
drawings.
