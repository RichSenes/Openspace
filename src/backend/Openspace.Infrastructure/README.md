# Openspace.Infrastructure

Adapters that connect the application to the outside world.

## Planned adapters

| Port | Adapter |
| --- | --- |
| Agent runtime | HTTP client and event stream for `opencode serve` |
| Agent tools | MCP server exposing `get_task`, `update_task_status`, `add_task_note`, `announce` |
| Workspaces | git worktrees, one per running task |
| Persistence | SQLite |

## Rules

- Implements interfaces declared in `Openspace.Application`.
- Never referenced by `Openspace.Domain` or `Openspace.Application`.
- Secrets (for example model provider keys) are never stored here: OpenCode
  owns its own provider configuration.

## Status

Empty.
