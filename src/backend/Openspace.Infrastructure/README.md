# Openspace.Infrastructure

Adapters that connect the application to the outside world.

## Planned adapters

| Port | Adapter |
| --- | --- |
| Agent runtime | `OpenCode/OpenCodeRuntime`: HTTP client for `opencode serve` (done) |
| Agent signals | `OpenCode/OpenCodeEventListener` + `OpenCodeEventTranslator`: global event stream to progress, permission, finished, failed (done) |
| Workspaces | `Git/GitWorkspaceManager`: one git worktree per task (done) |
| Queue | `Hosting/QueueRunnerService`: recovers at startup, fills seats every few seconds (done) |
| Persistence | SQLite via EF Core (done) |
| Agent tools | MCP server exposing `add_task_note`, `announce`, … (next) |

## Rules

- Implements interfaces declared in `Openspace.Application`.
- Never referenced by `Openspace.Domain` or `Openspace.Application`.
- Secrets (for example model provider keys) are never stored here: OpenCode
  owns its own provider configuration.

## Persistence

EF Core on SQLite.

| Table | Content |
| --- | --- |
| `Offices` | Offices |
| `Rooms` | Rooms, owned by an office |
| `Desks` | Desks with their agent profile, owned by a room |
| `WorkTasks` | Tasks; status stored as text |
| `TaskNotes` | Notes, owned by a task |

- Rooms, desks and notes are mapped as owned entities, so they are always loaded
  and saved with their aggregate.
- Strongly typed ids are stored as GUIDs; `DateTimeOffset` as a sortable 64-bit
  number, because SQLite cannot compare dates natively.
- The schema evolves through migrations in `Persistence/Migrations`. The API
  applies them at startup.

### Adding a migration

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/backend/Openspace.Infrastructure --output-dir Persistence/Migrations
```

## OpenCode

Openspace talks to one `opencode serve`, using its long-standing endpoints:
`POST /session`, `POST /session/{id}/prompt_async`, `POST /session/{id}/abort`,
`GET /session/{id}/message`, `GET /agent`, `POST /permission/{id}/reply` and the
server-sent events of `GET /global/event`. Every call passes `?directory=` with
the task's worktree, so a single server serves every task.

Events become speech bubbles like this:

| OpenCode event | Becomes |
| --- | --- |
| `todo.updated` | The item `in_progress`, in the agent's words: the bubble headline |
| `message.part.updated`, tool running | A short phrase: `Editing header.php`, `Running npm test`, `Searching the code` |
| `permission.asked` | The desk waits for you, with Allow once / Refuse |
| `session.idle` | The task goes to review; the agent's last answer becomes a note |
| `session.error` | The task fails with the reason (except aborts Openspace asked for) |

## Workspaces

Each task gets `git worktree add -b openspace/<title>-<key>` from the repository's
`HEAD`, under `Workspaces:Root`. Your checkout and branch are never touched.

- Diff: `git add --intent-to-add --all` then `git diff HEAD`, so new files show.
- Accept: commit on the task branch (with the repository's identity, or
  `Openspace` if none), then remove the worktree. The branch stays for you to merge.
- Discard: remove worktree and branch.

## Status

Persistence, OpenCode, workspaces and queue are done. MCP is next.
