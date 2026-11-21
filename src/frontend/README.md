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
  layout/     Top bar and its More menu, the strip of account and graphics card notices,
              office sidebar (a switcher on narrow screens)
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
  ui/         Toasts, dialogs, status pills, the one format for dates and times
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

**Rooms.** Rooms with their context, procedure and check (one save per room; the
routines' and guests' rooms show only what applies to them), desks with their name,
role, engine and model, one field per line with what each choice means under it. The
OpenCode agent name is checked as you type (lowercase letters, digits and dashes, as
the server wants it), so a wrong one never reaches the save.

**Office.** The office's own settings in one form with one save: name, repository
(with "Initialize git here" when the folder is not a repository yet), keeping the
project on this computer, holding a task while another desk changes the files it
names (on by default), private folders, and reserve models picked from the
engines' lists and put in order. A part the server refuses says why under its
field; the rest is saved.

**Routines.** Each routine with its schedule, settings, runs and log, what it
proposes (yes or no), and run now.

**The paper's editor.** The morning paper has a page of its own instead of a dialog: an
index of its parts, every setting as a field with its explanation under it, the sections
with their sources (each with **Test**, which reads the feed once and lists its latest news
or says why it cannot), the layout (text columns, picture sizes, sections of a full, half
or third width side by side, articles and their length) and, beside the form when there is
room or under it otherwise, the paper laid out with sample text as the settings change.
Telegram bots and local models are picked from lists; the JSON stays at the end. A section
may look further back than the paper, or print only on days with news about its words (the
artists or games the reader follows). Each section has a form (articles, in brief, main story
and briefs, explained simply); the layout can let the space follow the news; the paper takes
phrases never to write and a last proofreading.

**The job offers' editor.** The job offers routine has a page of its own too, built from the
same parts: where to look (Japan Dev, TokyoDev, LinkedIn and Daijob searches, companies' own
job pages), each place with **Test**; what the person looks for and does not want, cities,
Japanese, visa and the lowest salary; where the profile and the offer folders are; how many
offers are judged and from how many stars they arrive; the bot and the language.

**Reports.** Reports of a day or of an epic, and changelogs, in a chosen language,
each kind in its own card with what it tells: read them, copy them, download them.

**The supervisor's plans** run in the background: asking for one (from the Board, or handing
security findings over) never holds the page. While it plans, the status bar says so; when it
ends, a notice says what it made wherever you are, and a strip on top of the Board shows it,
with **Show them** and **Approve all**, for a quarter of an hour.

**Security.** The office's security audits: add a Security desk, start an audit (quick,
standard or deep, in a chosen language), follow its runs, read the findings (confirmed worst
first, those to verify, the rejected) and the full report, and hand the findings to the
supervisor, one or all at once, or let the audit do it by itself when it finishes.

**Agents.** Roles (the engines are in Settings): description, instructions (with an AI draft to review),
preferred model, what the agent may do without asking (a new role asks before
commands), and which installed skills it may load (all of them, or the ones ticked).
Ready-made roles (backend, frontend, mobile, database, tester, reviewer, security,
writer) are added with one click and then adjusted like any other.

**Settings.** Sections with an index beside them (a row of links on narrow screens),
one shown at a time and remembered: *Engines* (whether OpenCode and Claude Code
answer, where their keys live, the Claude account's limits), *Brain* (where Shiori's
vault is, what it holds by kind, "Open in Obsidian", a new folder for the next start),
*Local AI* (models on
this machine with a check that loads nothing, the programs that come first on the
graphics card, the small model for routines' questions; one save), *Models* (the
engine and model of each function: Shiori, who plans requests today, and the reviewer,
on Claude Code with an OpenCode model that stands in when it is missing; digests and
journals on Claude Code, OpenCode or a local model; the decider shown, with a link to
Local AI; models nobody lists flagged; one save), *How agents work* (permissions,
agents at work at once; one save), *Connections* (Google accounts,
Telegram), *Look and sound* (sounds, the weather's town), *System* (version,
backups). The engine warning in the top bar and the first steps open *Engines*.

Across every view:

- Under the views, a strip shows what accounts and the graphics card are up to (a
  provider over its limits, who uses the local model, the Claude account), only
  when there is something to say; it wraps on narrow windows, where the sound and
  connection words become icons.
- When a newer release is out, a banner says so with what changed, a link and
  "Remind me tomorrow" (remembered in this browser for that version). Openspace
  never updates itself.
- "Needs you" in the top bar counts everything that waits for you, from every office, and
  lists it by kind: agents asking (answered right there), tasks to approve, to review and
  failed (each opens on its office's Board with the task open), routines' proposals (yes
  and no right there), routines whose last run failed, and the brain's decisions: those
  Shiori accepted, to review (Fine, Undo), those left for you with the reason (Accept,
  Reject) and those due for review (Keep, Supersede). Their "Open" shows the note's text
  under the card; an answer takes the item away at once. It refreshes as tasks and
  routines change; on a phone the list takes the screen's width.
- Below 1100 px the layout changes instead of shrinking. The office list folds into one
  line with the current office and a "Switch office" button that opens the list over the
  view. The tabs that do not fit go behind a "More" button (Escape or a click elsewhere
  closes it, the arrows move inside it); the current view always keeps its tab. The Board
  puts the columns that need you first (Review, Failed, Proposed), and on phones stacks
  them so the page scrolls down, never sideways. The building's toolbar wraps, with its
  hint on a line of its own, and the hint goes away on phones.
- Without offices, the page is a short path to the first one: an engine, the office,
  rooms and desks, the repository, a first task, each with its button. A new office
  (no tasks, no routines) keeps the path in a corner of the building until it is
  hidden. The strip under the views warns when an engine the desks use is missing.
- Confirmations are short toasts at the bottom; errors stay next to the form that
  caused them, with the server's reason.
- Dates and times have one format: "today at 15:30", "yesterday at 23:05", else
  "Fri 30 Oct at 06:00", with the date and the clock (24 h or AM/PM) as the browser's
  language writes them.
- Fields have edges at 3:1 or more against every surface, as WCAG asks for controls.

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
