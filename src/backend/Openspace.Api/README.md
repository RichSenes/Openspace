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
| POST | `/api/tasks/{id}/discard` | Drop an open task |
| POST | `/api/tasks/{id}/notes` | Add a note as the operator: `{ text }` |
| GET | `/api/tasks/{id}/diff` | What the agent changed, as a unified diff (plain text) |
| GET | `/api/tasks/{id}/log` | The task's audit log, oldest first |
| GET | `/api/tasks/{id}/report` | A Markdown report of the task, as a download |
| POST | `/api/tasks/{id}/permission` | Answer an agent waiting for permission: `{ allow }` |
| POST | `/api/tasks/{id}/merge` | Merge accepted work into one of your branches: `{ target }`. `422` with code `uncommitted-changes`, `merge-conflict` or `branch-missing` when it cannot be done safely |
| GET | `/api/offices/{id}/branches` | `{ current, branches }`: branches work can be merged into (task branches hidden) |
| PUT | `/api/offices/{id}/repository` | Set the office's git repository: `{ path, initialize? }`. The folder must exist and be a git repository with a commit; with `initialize: true` Openspace runs `git init` and an empty first commit where needed |
| GET | `/api/activity?officeId=` | What every desk is doing right now |
| POST | `/api/offices/{id}/plan` | `{ request }`: the supervisor proposes tasks; returns `{ epic, tasks }`. `422` with `no-repository`, `no-desks`, `empty-text` or `planning-failed` |
| GET | `/api/epics?officeId=` | Epics of an office |
| POST | `/api/epics/{id}/approve` | Queue every proposal of the epic that has a desk; returns `{ approved }` |
| POST | `/api/offices/{id}/reports` | `{ from, to, scope, language }`: report of what happened in that period |
| POST | `/api/epics/{id}/report` | `{ language }`: report of an epic |
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

## Claude Code

Claude Code agents run as `claude -p` in the task's working copy, with `--restricted`:
personal and project settings never apply, only the role's tools. Reading is always
allowed; editing, commands and the web follow the role (allowed, asked, or not given at
all). Anything to be asked goes through the `permission` tool of Openspace's MCP server,
which waits for the person's answer in Openspace. Work counts against the limits of the
account Claude Code is signed in with, shared with everything else done with it.
