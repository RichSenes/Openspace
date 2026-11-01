# Tests

```bash
dotnet test                          # every backend project, from the repository root
cd src/frontend && npm test          # the page's logic (Vitest), next to the code as src/**/*.test.ts
```

| Project | Covers |
| --- | --- |
| `Openspace.ArchitectureTests` | The dependency rule between layers: Domain depends on nothing, Application only on Domain |
| `Openspace.Domain.Tests` | Offices, rooms, desks, layouts and floor plans; roles; the life of a task (statuses, rounds, procedure, checks, dependencies, reserves); epics, reports and routines |
| `Openspace.Application.Tests` | Use cases with in-memory fakes: the queue and why tasks wait, the task runner and agent signals, permissions in auto mode, loops, usage limits and reserve models, reviews, merges and checks, the supervisor, reports, the graphics card, guests, weather, releases and every kind of routine |
| `Openspace.Infrastructure.Tests` | SQLite round trips through the real migrations; OpenCode and Claude Code against fake servers and streams; git worktrees, merges and diffs on throwaway repositories; Google, Telegram, feeds, job boards, maintenance sources, weather and releases against fake HTTP handlers |
| `Openspace.Api.Tests` | Every endpoint on the real host, error mapping, SignalR messages and the MCP tools |

## Rules

- **Nothing real, ever.** No real OpenCode, Claude Code account, Google account,
  Telegram bot, graphics card, Claude session log, weather or release service:
  each has a fake, and the API tests switch the background services off
  (`OpenspaceApp`). A new adapter comes with its fake.
- **Made-up data.** Names, companies, places, mail and offers are invented; no
  credentials, even fake-looking ones from real services.
- **The count grows.** Every new feature adds tests; a fix adds the test that
  would have caught it.
- **Time is controlled.** Tests that depend on time use `FakeTimeProvider`, and
  waits are injected: none sleeps on the real clock.
