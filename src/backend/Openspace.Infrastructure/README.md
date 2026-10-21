# Openspace.Infrastructure

Adapters that connect the application to the outside world.

## Planned adapters

| Port | Adapter |
| --- | --- |
| Agent runtime | HTTP client and event stream for `opencode serve` |
| Agent tools | MCP server exposing `get_task`, `update_task_status`, `add_task_note`, `announce` |
| Workspaces | git worktrees, one per running task |
| Persistence | SQLite via EF Core (done) |

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

## Status

Persistence done. OpenCode, MCP and git adapters are next.
