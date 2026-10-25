# Openspace — Vision

> Codename: **EvilCorp**. Bonsoir, Elliot.

## The problem

Coding agents such as [OpenCode](https://opencode.ai) are powerful, but they work
inside a terminal. You get walls of code and tool output, and very little that
tells a human, at a glance, *who is doing what, why, and what actually changed*.

When the workload grows (several projects, each made of many services, each
with its own rules), juggling agents from a terminal does not scale. There is
no shared picture of the work, no board, no history you can read later.

## The idea

Openspace is a **visual control room for coding agents**, drawn as a pixel-art
office building:

- every **project** is an **office**;
- every office has **rooms**, one per area of competence (a CMS, an identity
  provider, a compliance topic, a test suite...);
- every room has one or more **desks**, and at each desk sits an **agent** with
  a well-defined role.

You walk through the building, see who is working, read what they are doing in
a speech bubble, assign tasks, review results and read reports. The agents
themselves run in OpenCode: Openspace is the director, not the actor.

## Principles

1. **OpenCode does the work.** Openspace drives `opencode serve` through its
   HTTP API and event stream. It never reimplements an agent runtime.
2. **Model-agnostic, small-model-first.** Whatever OpenCode can talk to works.
   Every feature must be usable with a small, fast model, not only with frontier
   ones.
3. **Limited seats.** The machine is local: only a configurable number of agents
   may run at the same time. Everyone else waits in a queue, visibly asleep.
4. **Humans approve.** Plans proposed by a supervisor and changes produced by
   agents are reviewed before they become real.
5. **Meaningful feedback.** A bubble says *"Adding the requested button to the
   header"*, not *"edited 3 files"*.
6. **Agents keep the books.** Agents update tasks and notes themselves, so the
   record of what was done is always current.

## Glossary

| Term | Meaning |
| --- | --- |
| **Building** | The whole Openspace instance. |
| **Office** | A project. Owns one or more repositories, its rooms and its work. |
| **Room** | An area of competence inside an office, with its own context notes and paths. |
| **Desk** | A seat in a room, bound to an OpenCode agent and optionally to a role. |
| **Role** | A reusable kind of agent: instructions, preferred model and what it may do without asking. |
| **Task** | A unit of work assigned to a desk. Has a status, a requester and notes. `WorkTask` in code. |
| **Supervisor** | Reads the project and breaks a request down into tasks proposed for the right desks. |
| **Epic** | A larger request and the tasks it was split into. |
| **Queue** | Orders runnable tasks and hands them to free seats. |
| **Seat** | One of the N execution slots. A desk is "awake" only while holding a seat. |
| **Bubble** | A short, human sentence describing what a desk is doing right now. |
| **Report** | A readable summary of what happened: per task, per day, per office. |
| **Skin** | The sprite set used to draw an agent, chosen by agent, role, office or mood. |

## Where bubbles come from

1. **OpenCode todo list**: agents already keep a list of steps; the current one
   is a good bubble for free.
2. **An `announce` tool** given to agents: before each step they state, in one
   sentence, what they are doing and why.
3. **Fallback summarizer**: if a desk stays silent too long, a small model
   summarizes its latest events into one sentence.

## First prototype (MVP)

In scope:

- offices, rooms and desks defined in a configuration file;
- create a task by hand and assign it to a desk;
- a queue with N seats;
- run the task through `opencode serve`, in its own git worktree;
- live event stream turned into bubbles and an activity feed;
- when done: show the diff, accept or discard, with the agent's notes attached to the task;
- a minimal MCP server so agents can update their task and announce bubbles;
- one pixel-art office view with basic animations (idle, working, sleeping).
  Done: agents drawn in their real state, office themes, lights off.

Later, in rough order of appearance:

- ~~**Claude Code.**~~ Done: desks choose their engine, OpenCode with any of its
  models (picked from the list OpenCode gives) or Claude Code with the person's
  own account. Claude Code plans, drafts and reviews finished work with a note
  before a person does; the Agents page shows how much of the account is used.
- ~~**Lessons of the first real office.**~~ Done: auto permissions that ask only
  about risky actions, bubbles and requests in plain words, one "needs you" button
  instead of pop-ups, a card for each agent in the building, send back with the
  review, rounds of work, checks to try by hand before merging, tokens per round
  and per desk, and a Settings view.
- ~~**After the second run.**~~ Done: a Merge desk that brings all accepted work
  into a branch at once and sends what conflicts back to its agent to resolve;
  a changelog for the people using the product; a mood per room (bright studio,
  dark focus, town hall and the office themes); notes newest first.
- **More room moods.** Drawn, not only painted: a dusty storeroom with cobwebs, a
  living room with a sofa and a TV, a scientists' lab with glassware and capsules.
  A second round of decorations and rooms, done together with the sprite lab,
  once the important work is closed.
- ~~**Running it day to day**~~ Done: checks after a merge (the rooms' own
  check commands, run on the merged branch); the queue pausing Claude Code desks
  while the account is over its limits and resuming them after; an archive in the
  Board (search, merged work hidden); one command to start, stop and restart
  everything; automatic database backups. Then task dependencies, merge requests,
  sounds and the rest, in the order below.
- ~~**Private folders.**~~ Done: an office lists folders of the person's own
  (photos, models, personal documents) that every agent, planner and reviewer is
  told about and that Openspace, OpenCode and Claude Code all refuse.
- ~~**Task dependencies.**~~ Done: the supervisor says which proposed tasks wait for others
  (a fix that needs an analysis first); the queue holds them until those are
  accepted, and the Board shows what each one waits for.
- **Local models and routines.** Small models running on this machine (through
  Ollama or similar, wired into OpenCode) for simple recurring jobs such as sorting
  email, on a schedule, outside the office and its repositories.

- ~~**Notification sounds.**~~ Done: a sound when an agent needs you, when work reaches
  review, when a task fails, and other moments worth hearing. Customisable per
  kind of event, per room or per single desk, with sensible defaults and a quick
  way to mute.
- ~~**Office layout editor.**~~ Done: Arrange in the building view resizes a room,
  drags desks and furniture anywhere (arrows nudge, Delete removes) and saves the
  room; everything is drawn sorted by its base line. Rooms never arranged keep
  the automatic layout.
- ~~**One building.**~~ Done: an office is drawn as one building. Rooms of a row
  share their walls and open through a door onto the corridor in front of them;
  whatever is not a room is common floor, furnished from the theme's corridor list
  (water cooler, vending machine, plants…).
- ~~**Floor plans.**~~ Done: in Arrange the building keeps the size you give it and
  never follows the screen; rooms are dragged by their back wall anywhere they fit,
  doors slide along any wall (shared walls and outer walls open too), the common
  floor is furnished by hand, and windows in back walls show the sky for the hour
  of the day on an outer wall, or whatever stands behind an inner one.
- ~~**Shapes other than rectangles.**~~ Done: "Cut a corner" takes a rectangle out
  of the building; dragged and stretched by any edge it makes an L, T or U at an
  edge, or a courtyard inside. Walls follow the outline, rooms and furniture stay
  out of the cuts, and windows facing one show the sky.
- ~~**Agents dressed by hand.**~~ Done: "Look" beside each desk in Rooms, or "Change
  look" on a desk in Arrange, picks skin, hair, clothes, outfit, accessory or a
  costume with the agent shown typing as it will be; desks never dressed keep the
  look picked from their id.
- **The city.** A map of the town where each office is a building (a house, a
  tower, a warehouse) among roads with moving cars, random decorations and light
  that follows the time of day. Clicking a building zooms into it and fades to
  its rooms; leaving zooms back out to the street.
  The town has **districts**, each with its own style, so different kinds of
  projects are told apart at a glance: work offices in a business district of
  glass and towers, personal projects in a residential one of houses and gardens,
  creative projects in an arts quarter of warehouses and murals. Each office
  belongs to a district, which decides where its building stands and how it is
  drawn; projects far apart from each other sit in different districts.
- ~~**Mouse navigation**~~ Done: wheel to zoom at the cursor, left-drag to pan,
  double-click a room to fly into it, Fit to see everything again.
- ~~**Audit log.**~~ Done: every tool call, step, permission, note and status
  change is stored per task, shown as a live timeline and exported as a Markdown
  report.
- **Servers as rooms.** A room can target a remote machine instead of a git
  repository: its desks work over SSH with the room's host, user and key
  reference (keys stay in the SSH agent or OpenCode's configuration, never in
  Openspace). Such rooms ask permission for every command by default.
- ~~**Procedures.**~~ Done: a room's required steps go into the brief, become a
  checklist on the task that the agent fills in with evidence through
  `complete_step`, and work does not reach review until every step is reported.
- ~~**Merge requests**~~ Done: push from the Merge desk, never forced; GitHub pull
  requests for accepted work, checked back so merged ones count as merged; offices
  kept on this machine are never pushed.
- **Epics**: large goals, made of tasks;
- **Proposals**: ideas or plans waiting for a human decision (a supervisor's
  breakdown is a proposal too);
- **Tasks**: units of work assigned to desks;
- **Notes**: what an agent did, found or left open, attached to a task.

Agents keep it up to date themselves. Openspace exposes an **MCP server** that
OpenCode agents connect to, with the tools `get_task`, `add_task_note` and
`announce` (done). This is how "what was done and what was not"
stays tracked without a human copying it anywhere.

Each room keeps **context notes** (conventions, paths, do and don't) that are
injected into the agent's prompt when a task starts.

Everything is stored in SQLite. Markdown export (for example to an Obsidian
vault, with front matter for Kanban and Dataview) is a possible later addition,
not a dependency.

## Architecture (draft)

- **Backend**: .NET, Clean Architecture with a rich domain model (DDD).
  `Domain`, `Application`, `Infrastructure` (OpenCode client, MCP server, git,
  persistence), `Api` (ASP.NET Core, SignalR for live updates).
- **Frontend**: TypeScript, PixiJS for the building, React for panels and board.
  Sprites as PNG sheets with Aseprite JSON.

## Open questions

- Which events are worth showing in the activity feed and which are noise.
- How offices talk to each other when one project depends on another.
