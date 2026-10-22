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
| PUT | `/api/offices/{id}/rooms/{roomId}/layout` | Arrange a room by hand: `{ width, depth, desks: [{ deskId, x, y }], furniture: [{ name, x, y }] }`; sizes in tiles, places in pixels from the room's corner; desks left out are placed automatically |
| DELETE | `/api/offices/{id}/rooms/{roomId}/layout` | Give the room back to the automatic layout |
| POST | `/api/offices/{id}/rooms/{roomId}/desks` | Add a desk: `{ name, agentName, model?, roleId? }` |
| GET | `/api/tasks?officeId=&status=` | Tasks, oldest first, optionally filtered |
| GET | `/api/tasks/{id}` | One task with its notes |
| POST | `/api/tasks` | `{ officeId, deskId?, title, description? }`: queued with a desk, proposed without |
| POST | `/api/tasks/{id}/approve` | Approve a proposed task: `{ deskId? }` |
| POST | `/api/tasks/{id}/accept` | Accept work in review |
| POST | `/api/tasks/{id}/send-back` | Back to the queue with feedback: `{ text }` |
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

Tools only act on tasks whose agent is working; otherwise they answer with an
error the agent can read. There is no authentication: the endpoint is meant for
OpenCode running on the same machine.

## Live updates

SignalR hub at `/hubs/building`, server to client only:

| Message | Payload | Sent when |
| --- | --- | --- |
| `OfficeChanged` | office id | an office, room or desk changes |
| `TaskChanged` | task id | a task is created, changes status or gets a note |
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
| `Operator:Name` | `operator` | Who you are: recorded as requester and note author |
| `Queue:Enabled` | `true` | Run the queue in the background |
| `Queue:PollInterval` | `00:00:02` | How often free seats are filled |
| `OpenCode:BaseUrl` | `http://127.0.0.1:4096` | Where `opencode serve` listens |
| `OpenCode:ListenToEvents` | `true` | Follow the OpenCode event stream |
| `OpenCode:DraftModel` | none | Model for AI drafts and supervisor plans; none means OpenCode's default |
| `OpenCode:ToolsUrl` | `http://127.0.0.1:5145/mcp` | Where OpenCode reaches this API's MCP tools; change it with the port |
| `Workspaces:Root` | `~/.local/share/openspace/worktrees` | One git worktree per task lives here |

`appsettings.json` holds non-secret defaults. Local overrides go in
`appsettings.Local.json` (ignored by git) or environment variables, for example
`Queue__Seats=2`.
