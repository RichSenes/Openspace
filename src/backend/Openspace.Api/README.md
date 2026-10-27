# Openspace.Api

ASP.NET Core host. Composes the layers and exposes them to the frontend.

## Endpoints

| Method | Path | Description |
| --- | --- | --- |
| GET | `/health` | Liveness check, returns `{ "status": "ok" }` |
| GET | `/api/offices` | All offices with rooms and desks |
| GET | `/api/offices/{id}` | One office |
| POST | `/api/offices` | Open an office: `{ name }` |
| PUT | `/api/offices/{id}/theme` | Draw the office with another mood: `{ theme }`, one word such as `cozy` or `neonDistrict` |
| POST | `/api/offices/{id}/rooms` | Open a room: `{ name }` |
| PUT | `/api/offices/{id}/rooms/{roomId}/procedure` | Set the room's required procedure: `{ steps: [] }`; applies to tasks started from then on |
| PUT | `/api/offices/{id}/rooms/{roomId}/context` | Set a room's context: `{ context }` |
| PUT | `/api/offices/{id}/private-paths` | `{ paths }`: absolute folders no agent of the office may touch, replacing the list (20 at most) |
| PUT | `/api/offices/{id}/rooms/{roomId}/check` | `{ command }`: what tells whether the room's code works, run after merges; `null` removes it |
| POST | `/api/offices/{id}/checks` | `{ branch, taskIds? }`: run the rooms' checks on a branch, in a throwaway copy. With `taskIds`, only their rooms, and each task gets a note with the result. Returns `[{ roomId, room, command, passed, exitCode, output, seconds }]` |
| PUT | `/api/offices/{id}/rooms/{roomId}/mood` | Draw one room with its own theme: `{ mood }`, or `null` to follow the office |
| PUT | `/api/offices/{id}/rooms/{roomId}/layout` | Arrange a room by hand: `{ width, depth, desks: [{ deskId, x, y }], furniture: [{ name, x, y }], door?: { wall, offset }, windows?: [x] }`; sizes in tiles, places in pixels from the room's corner; `wall` is `Front`, `Back`, `Left` or `Right`, `offset` in tiles; desks left out are placed automatically. On a floor plan the room must still fit |
| PUT | `/api/offices/{id}/plan` | Draw the building by hand: `{ width, depth, furniture: [{ name, x, y }], rooms: [{ roomId, left, top, layout }], cuts?: [{ left, top, width, height }] }`; rooms and cuts in tiles from the building's corner, never overlapping; cuts take pieces out of the rectangle for an L, T or U shape or a courtyard, and nothing stands in them; `layout` as above |
| DELETE | `/api/offices/{id}/plan` | Give the whole office back to the automatic layout |
| DELETE | `/api/offices/{id}/rooms/{roomId}/layout` | Give the room back to the automatic layout |
| POST | `/api/offices/{id}/rooms/{roomId}/desks` | Add a desk: `{ name, agentName, model?, roleId? }` |
| GET | `/api/tasks?officeId=&status=` | Tasks, oldest first, optionally filtered |
| GET | `/api/tasks/{id}` | One task with its notes |
| POST | `/api/tasks` | `{ officeId, deskId?, title, description? }`: queued with a desk, proposed without |
| POST | `/api/tasks/{id}/approve` | Approve a proposed task: `{ deskId? }` |
| POST | `/api/tasks/{id}/accept` | Accept work in review |
| POST | `/api/tasks/{id}/send-back` | Back to the queue for another round: `{ text?, withReview? }`; with `withReview` the reviewer's note on this round goes along, after the text or alone |
| PUT | `/api/tasks/{id}/checks/{number}` | Tick a check a person tried by hand, or untick it: `{ done }`; in review or done |
| POST | `/api/tasks/{id}/retry` | Requeue a failed task |
| POST | `/api/tasks/{id}/stop` | Stop a running agent and put its work so far in review, as it is |
| POST | `/api/tasks/{id}/discard` | Drop an open task |
| POST | `/api/tasks/{id}/notes` | Add a note as the operator: `{ text }` |
| GET | `/api/tasks/{id}/diff` | What the agent changed, as a unified diff (plain text) |
| GET | `/api/tasks/{id}/log` | The task's audit log, oldest first |
| GET | `/api/tasks/{id}/report` | A Markdown report of the task, as a download |
| POST | `/api/tasks/{id}/permission` | Answer an agent waiting for permission: `{ allow }` |
| POST | `/api/tasks/{id}/merge` | Merge accepted work into one of your branches: `{ target }`. `422` with code `uncommitted-changes`, `merge-conflict`, `branch-missing` or `task-branch-missing` when it cannot be done safely |
| POST | `/api/offices/{id}/merge` | `{ target, taskIds? }`: merge all accepted work not merged yet (or the listed tasks), first accepted first. Returns `[{ taskId, title, result, message }]` with `result` `Merged`, `Conflict`, `Failed` or `NotTried`; uncommitted changes or a missing target stop the round |
| PUT | `/api/tasks/{id}/dependencies` | `{ taskIds }`: the tasks of the same office this one waits for (10 at most); it starts once each is accepted and merged. `422` for a loop or a task already started |
| POST | `/api/tasks/{id}/catch-up` | `{ target }`: send accepted work that conflicts back to its desk as a new round. Openspace starts merging the target into the task branch in a fresh working copy; the agent resolves the conflicts |
| POST | `/api/offices/{id}/push` | `{ branch }`: send one of your branches to its remote (origin when it follows none), after a fetch; `422` `behind-remote` when the remote has commits it lacks. Never forced |
| POST | `/api/tasks/{id}/pull-request` | `{ target }`: push the accepted work's branch and open a GitHub pull request into the target, with the agent's summary and the checks to try by hand. Returns `{ url }` |
| POST | `/api/tasks/{id}/pull-request/check` | `{ state }`: how the pull request stands on GitHub. Merged, the work counts as merged and the target here is fast-forwarded when it can be; closed, it can be merged again |
| PUT | `/api/offices/{id}/local-only` | `{ localOnly }`: keep the project on this machine; push and pull requests are then refused |
| GET | `/api/offices/{id}/branches` | `{ current, branches }`: branches work can be merged into (task branches hidden) |
| PUT | `/api/offices/{id}/repository` | Set the office's git repository: `{ path, initialize? }`. The folder must exist and be a git repository with a commit; with `initialize: true` Openspace runs `git init` and an empty first commit where needed |
| GET | `/api/activity?officeId=` | What every desk is doing right now |
| POST | `/api/offices/{id}/plan` | `{ request }`: the supervisor proposes tasks; returns `{ epic, tasks }`. `422` with `no-repository`, `no-desks`, `empty-text` or `planning-failed` |
| GET | `/api/epics?officeId=` | Epics of an office |
| POST | `/api/epics/{id}/approve` | Queue every proposal of the epic that has a desk; returns `{ approved }` |
| POST | `/api/offices/{id}/reports` | `{ from, to, scope, language }`: report of what happened in that period |
| POST | `/api/epics/{id}/report` | `{ language }`: report of an epic |
| POST | `/api/offices/{id}/changelog` | `{ language }`: changelog of the work accepted since the previous changelog, for the people using the product. `422` when nothing new was accepted |
| GET | `/api/offices/{id}/reports` | Reports of an office, newest first |
| GET | `/api/reports/{id}/download` | The report as a Markdown file |
| GET | `/api/roles` | All roles |
| POST | `/api/roles` | Define a role: `{ name, description?, instructions?, model?, edit, bash, web }` with `Allow`, `Ask` or `Deny` |
| PUT | `/api/roles/{id}` | Change a role (same body) |
| DELETE | `/api/roles/{id}` | Delete a role; `422` with code `role-in-use` while desks use it |
| POST | `/api/roles/draft` | `{ name, description }` → `{ instructions }` drafted by the AI, not saved |
| PUT | `/api/offices/{id}/desks/{deskId}/name` | Rename a desk: `{ name }` (unique within its room) |
| PUT | `/api/offices/{id}/desks/{deskId}/agent` | What runs the desk's agent: `{ engine, agentName, model? }`; `engine` is `OpenCode` (model as `provider/model`) or `ClaudeCode` (model such as `sonnet` or `claude-sonnet-5-5`); applies from the desk's next task |
| GET | `/api/engines` | What can run agents here: OpenCode and its models, Claude Code and its models, the Claude account's usage, which engine supervises and which reviews |
| POST | `/api/engines/claude-usage` | Reads the Claude account's usage now, with a one-word question to Haiku; `409` when Claude Code is not there |
| GET | `/api/usage/desks` | Tokens each desk spent over all its tasks, the most first |
| GET | `/api/settings` | What a person can change from Openspace: `{ autonomy, seats, claudeSupervises, supervisorModel, claudeReviews, reviewModel, draftModel }` |
| PUT | `/api/settings` | Change them (same body): written to the settings file and applied at once |
| GET | `/api/sounds` | `{ muted, volume, rules, files }`: which sound each moment plays (`needs-you`, `question`, `review`, `verdict`, `failed`, `plan-ready`, `checks-failed`, `paused`, `all-done`, `offline`), in general or for one room or desk, and your own sound files |
| PUT | `/api/sounds` | `{ muted, volume, rules }`, each rule `{ event, roomId?, deskId?, sound }` with `sound` = `builtin:<name>`, `file:<name>` or `silent` |
| POST | `/api/sounds/files` | A sound file of yours (form field `file`; mp3, ogg, opus, wav, m4a, webm or flac, 3 MB at most), kept on this machine |
| GET / DELETE | `/api/sounds/files/{name}` | Play it, or delete it: the rules that used it play their default again |
| GET | `/api/backups` | Copies of the database, newest first: `[{ name, path, size, createdAt }]` |
| POST | `/api/backups` | Copy the database now; `422` when it lives in memory |
| PUT | `/api/offices/{id}/desks/{deskId}/look` | Dress the desk's agent: `{ skin, hair, hairStyle, cloth, accessory?, outfit?, costume? }`, single words the frontend knows; unknown ones are drawn with its defaults |
| DELETE | `/api/offices/{id}/desks/{deskId}/look` | Give the agent back the look picked from the desk's id |
| PUT | `/api/offices/{id}/desks/{deskId}/role` | `{ roleId }` (null to remove); applies from the desk's next task |

Enums travel as strings (`"Queued"`). Errors are
[problem details](https://www.rfc-editor.org/rfc/rfc9457): `404` for unknown ids,
`422` when a rule says no, with the reason in `detail`. When the interface can
offer a fix, the problem also carries a `code`: `folder-missing`,
`not-a-repository` or `no-commits` for repositories.

## Tools for agents (MCP)

`/mcp` is a [Model Context Protocol](https://modelcontextprotocol.io) server
(streamable HTTP, stateless). When a task starts, Openspace registers it in
OpenCode for that task's worktree as the remote server `openspace`, with the
header `X-Openspace-Task: <task id>`, so every call is tied to its task without
the agent having to name it.

| Tool | What it does |
| --- | --- |
| `get_task` | Returns the brief again: room rules, request, team notes |
| `add_task_note` | `{ text }`: a note on the task signed by the desk |
| `announce` | `{ text }`: sets the desk's speech bubble and logs the step |
| `complete_step` | `{ step, evidence }`: reports a step of the task's required procedure as done |
| `add_manual_check` | `{ what, how }`: lists something a person must try by hand before merging, and how |
| `wait_for_person` | `{ question }`: the agent waits, spending nothing, until the person does or decides something (close an app, say) and answers "Done, go on" or "Stop" |
| `permission` | Called by Claude Code itself before a tool its role does not allow outright |

Tools only act on tasks whose agent is working; otherwise they answer with an
error the agent can read. There is no authentication: the endpoint is meant for
OpenCode running on the same machine.

## Live updates

SignalR hub at `/hubs/building`, server to client only:

| Message | Payload | Sent when |
| --- | --- | --- |
| `OfficeChanged` | office id | an office, room or desk changes |
| `TaskChanged` | task id | a task is created, changes status, gets a note or a check, or spends tokens |
| `DeskActivity` | the activity | an agent moves to a new step or action, waits, or stops |
| `TaskLogChanged` | task id | a tool call, step or permission was added to the task's log |
| `RolesChanged` | none | a role was defined or changed |
| `EpicsChanged` | none | the supervisor opened an epic |
| `ReportsChanged` | none | a report was written |

`OfficeChanged` and `TaskChanged` carry ids and clients refetch; they come from
domain events after each successful save. `DeskActivity` carries the data itself,
because activity is not stored.

## Run

```bash
dotnet run --project src/backend/Openspace.Api
```

Listens on `http://localhost:5145` in development. To run a second instance next
to it (for example for experiments), skip the launch profile, which would force
port 5145, and give it its own database:

```bash
ConnectionStrings__Openspace="Data Source=/tmp/other.db" ASPNETCORE_URLS=http://localhost:5245 \
  dotnet run --no-launch-profile --project src/backend/Openspace.Api
```

On startup it applies pending database migrations.

## Configuration

| Key | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings:Openspace` | `Data Source=openspace.db` | SQLite database file |
| `Queue:Seats` | `1` | Agents allowed to work at the same time |
| `Agents:Autonomy` | `Auto` | `Auto`: Openspace answers agents' requests itself and asks a person only about risky ones; `Ask`: every request waits for a person |
| `Settings:Path` | `openspace.settings.json` | Where changes made in Settings are kept; it overrides the other files |
| `Checks:TimeoutMinutes` | `20` | How long one room's check may run before it is stopped |
| `Backups:Folder` | `backups` | Where database copies go, next to the database unless absolute |
| `Backups:Keep` | `14` | Copies kept; older ones are deleted |
| `Backups:EveryHours` | `24` | How often a copy is taken while the API runs |
| `Sounds:Folder` | `sounds` | Where sound choices and your own sound files are kept; ignored by git |
| `GitHub:Command` | `gh` | The GitHub CLI, signed in with `gh auth login`; also looked for in `~/.local/bin` |
| `Operator:Name` | `operator` | Who you are: recorded as requester and note author |
| `Queue:Enabled` | `true` | Run the queue in the background |
| `Queue:PollInterval` | `00:00:02` | How often free seats are filled |
| `OpenCode:BaseUrl` | `http://127.0.0.1:4096` | Where `opencode serve` listens |
| `OpenCode:ListenToEvents` | `true` | Follow the OpenCode event stream |
| `OpenCode:DraftModel` | none | Model for AI drafts and supervisor plans when OpenCode makes them; none means OpenCode's default |
| `ClaudeCode:Enabled` | `true` | Use Claude Code where it is installed: desks may run on it, and it supervises and reviews |
| `ClaudeCode:Command` | `claude` | The Claude Code command; it runs with the account it is signed in with, never with an API key of Openspace |
| `ClaudeCode:Supervise` | `true` | Claude Code plans and drafts; when it fails (limits reached, say) OpenCode does it |
| `ClaudeCode:Review` | `true` | Claude Code reads finished work and leaves a note before a person reviews it |
| `ClaudeCode:SupervisorModel`, `ClaudeCode:ReviewModel` | none | Models for plans and reviews, such as `opus` or `sonnet`; none means Claude Code's default |
| `OpenCode:ToolsUrl` | `http://127.0.0.1:5145/mcp` | Where OpenCode reaches this API's MCP tools; change it with the port |
| `Workspaces:Root` | `~/.local/share/openspace/worktrees` | One git worktree per task lives here |

`appsettings.json` holds non-secret defaults. Local overrides go in
`appsettings.Local.json` (ignored by git) or environment variables, for example
`Queue__Seats=2`. What you change in the Settings view is written to
`openspace.settings.json` (ignored by git too), which comes last and wins; it is
read again at once, so those settings need no restart.

## Permissions in auto mode

A role says what an agent may do without asking. Beyond that, in auto mode
Openspace answers requests itself and records each one in the task's log
("Allowed on its own: run the tests · npm test"). A person still decides when a
request pushes or deploys, reaches a live service, changes the machine or
another one, goes outside the task's working copy, deletes recursively above it,
sends data to the web, runs a script piped from the web, or touches `.env`
files, keys and credentials. The same rules apply to OpenCode and Claude Code.

## Rounds, checks and tokens

A task's first round of work is 1; each send back starts the next. Agents list
what a person must try by hand with `add_manual_check`, the reviewer adds what
is missing, and ticks are cleared when the work goes back. Tokens are counted
per round, as OpenCode reports each answer and Claude Code each run.

## Agents going in circles

Smaller models, local ones above all, can get stuck: the same edit failing the same
way again and again, or a conversation that fills the model's context, is summarised
by OpenCode and loses its thread. Openspace watches for both:

- the same tool call failing the same way 4 times with no change landing in between;
- the conversation summarised 3 times in the same session (`session.compacted`).

The agent is then stopped and the task goes back to the queue with a note; it starts
again in a fresh conversation, in the same working copy, with a brief that says what
went wrong and asks it to look at `git diff` before doing anything again. A task gets
two fresh starts per round; looping a third time, it goes to review as it is, missing
steps included, so a person (or the reviewer) can accept it or send it back with
directions. Nothing done is ever thrown away. A person can do the same at any time
with Stop and review (`POST /api/tasks/{id}/stop`).

## Sounds

The hub announces `TaskCue` `{ taskId, officeId, deskId, cue }` when work reaches
review (`review`), a task fails (`failed`), Claude's verdict comes in (`verdict`)
or an agent stopping leaves its office with nothing running or queued
(`all-done`); a desk starting to wait for a permission or for an answer comes
with its `DeskActivity`. The page itself notices a plan being ready, checks
failing after a merge, an account pausing for its limits and the connection
dropping. The page plays the
sound the rules give that moment at that desk: the desk's rule first, then its
room's, then the general one, then the default. Built-in sounds are drawn in code,
in the spirit of the pixel art.

## Private folders

An office can list folders of the person's own that no agent may touch: photos,
models, personal documents. Every layer refuses them:

- the brief of every agent, and the instructions of the planner and of the
  reviewer, list them as off limits;
- OpenCode sessions get deny rules for them (read, edit, search, list, folders
  outside the copy, and any command naming them), after everything else, so no
  earlier rule lets them through;
- Claude Code runs get `--disallowedTools` rules for reading (which covers
  searching), editing and any command naming them;
- any permission request naming them is refused at once, in any mode, without
  asking anyone, and the task gets a note saying so;
- in such offices a command climbing above the working copy with `..` is never
  allowed on its own.

A project's own publish or deploy script (`tools/publish.sh`, `deploy.ps1`…)
always waits for a person, in any mode.

## Usage limits

When an account is over its usage limits, its tasks go back to the queue
instead of failing or hanging, and the desks spending it take no work until the
reset; the others go on. Claude Code says so when a run stops; OpenCode retries
silently, and Openspace reads its `session.status` "retry" events (a provider's
free tier running out, say), stops the session and pauses the desks on that
provider. Once a minute it also asks OpenCode how running tasks' sessions are,
so a session that was already retrying when Openspace started is noticed too. `GET /api/engines` lists the pauses (`pauses`, and
`claudePausedUntil`); without a reset time a pause lasts 30 minutes. "Check
now" in Settings ends Claude's pause early once the account answers again.

### Reserve models

An office can list up to four OpenCode models as reserves
(`PUT /api/offices/{id}/reserve-models` with `{ "models": ["provider/model"] }`,
or Rooms). When a queued task's desk is paused, the queue hands it to the first
reserve whose own provider is neither paused nor the desk's, writes a note on the
task and shows the model in its `reserveModel`. An OpenCode desk keeps its agent
with the reserve model; a Claude Code desk lends its role, instructions and
private folders to OpenCode's `build` agent, which takes over the session. A
reserve over its limits hands over to the next one; once the desk's account is
back, the task returns to it the next time it is queued. Free tiers may keep what
they are sent, so an office kept on this machine (`localOnly`) cannot have
reserves, and one with reserves cannot be kept local until they are removed.

### Local models and the graphics card

With `LocalModels:Enabled`, desks can name models served on this machine as
`local/<id>` (the provider name is `LocalModels:Provider`). Each model says what
starts its server, a command that stays in the foreground and answers an
OpenAI-compatible API at `LocalModels:ServerUrl`, and how much graphics memory it
needs. Keep real paths in `appsettings.Local.json`, which git ignores:

```json
"LocalModels": {
  "Enabled": true,
  "ApiKeyFile": "/opt/models/.apikey",
  "Models": [
    { "Id": "gemma", "Name": "Gemma", "Command": "/opt/models/start-gemma.sh", "MemoryMiB": 11000, "Context": 32768 }
  ],
  "Holders": [
    { "Name": "Image studio", "Match": "image-studio" }
  ]
}
```

At startup the API writes the matching OpenCode provider to
`LocalModels:OpenCodeConfigPath` (the key is read from `ApiKeyFile` by OpenCode,
never copied); `scripts/openspace` points it to the state folder and hands it to
`opencode serve`, so restart OpenCode once after the first time.

Every few seconds the API looks at the card, and only at the memory in use
(`nvidia-smi`) and at the running programs' command lines (Linux):

- A queued task on a local model waits until that model answers; the model is
  started when the card has the memory it needs and nothing else answers at the
  server's address. One local task runs at a time.
- A model nobody used for `IdleUnload` (10 minutes) is stopped. A desk waiting
  for another model gets it once the loaded one is idle.
- A holder is a program that comes first: one whose command line contains its
  `Match`, or one that claims the card with `POST /api/gpu/claim`
  (`{ "holder": "Image studio" }`, renewed within `ClaimLease`, 10 minutes, and
  released with `DELETE /api/gpu/claim/{holder}`). Local tasks then have
  `YieldGrace` (3 minutes) to end; after that they go back to the queue with a
  note and resume in their own session later, and the model is stopped.
  Pick a `Match` only that program has: any command line that contains it counts.
- A model that does not answer within `StartTimeout` is stopped and tried again
  after `RetryAfter`; its server's last lines go to the API log.

`GET /api/gpu` (also in `GET /api/engines` as `gpu`) says who has the card:
`Off`, `Free`, `Loading`, `Serving`, `WaitingForMemory`, `Yielding`, `Lent` or
`Failed`, with the model, the holders and a sentence. The top bar shows it. When
Openspace stops, it stops the model it started.

## Claude Code

Claude Code agents run as `claude -p` in the task's working copy, with `--restricted`:
personal and project settings never apply, only the role's tools. Reading is always
allowed; editing, commands and the web follow the role (allowed, asked, or not given at
all). Anything to be asked goes through the `permission` tool of Openspace's MCP server,
which waits for the person's answer in Openspace. Work counts against the limits of the
account Claude Code is signed in with, shared with everything else done with it.
