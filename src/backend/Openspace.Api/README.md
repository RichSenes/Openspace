# Openspace.Api

ASP.NET Core host. Composes the layers and exposes them to the frontend.

## Endpoints

| Method | Path | Description |
| --- | --- | --- |
| GET | `/health` | Liveness check, returns `{ "status": "ok" }` |
| GET | `/api/offices` | All offices with rooms and desks |
| GET | `/api/offices/{id}` | One office |
| POST | `/api/offices` | Open an office: `{ name }` |
| POST | `/api/offices/{id}/rooms` | Open a room: `{ name }` |
| PUT | `/api/offices/{id}/rooms/{roomId}/context` | Set a room's context: `{ context }` |
| POST | `/api/offices/{id}/rooms/{roomId}/desks` | Add a desk: `{ name, agentName, model? }` |
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
| POST | `/api/tasks/{id}/permission` | Answer an agent waiting for permission: `{ allow }` |
| PUT | `/api/offices/{id}/repository` | Set the office's git repository: `{ path }` (absolute) |
| GET | `/api/activity?officeId=` | What every desk is doing right now |

Enums travel as strings (`"Queued"`). Errors are
[problem details](https://www.rfc-editor.org/rfc/rfc9457): `404` for unknown ids,
`422` when a domain rule says no, with the reason in `detail`.

## Live updates

SignalR hub at `/hubs/building`, server to client only:

| Message | Payload | Sent when |
| --- | --- | --- |
| `OfficeChanged` | office id | an office, room or desk changes |
| `TaskChanged` | task id | a task is created, changes status or gets a note |
| `DeskActivity` | the activity | an agent moves to a new step or action, waits, or stops |

`OfficeChanged` and `TaskChanged` carry ids and clients refetch; they come from
domain events after each successful save. `DeskActivity` carries the data itself,
because activity is not stored.

## Run

```bash
dotnet run --project src/backend/Openspace.Api
```

Listens on `http://localhost:5145` in development.

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
| `Workspaces:Root` | `~/.local/share/openspace/worktrees` | One git worktree per task lives here |

`appsettings.json` holds non-secret defaults. Local overrides go in
`appsettings.Local.json` (ignored by git) or environment variables, for example
`Queue__Seats=2`.
