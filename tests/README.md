# Tests

| Project | Covers |
| --- | --- |
| `Openspace.ArchitectureTests` | Clean Architecture dependency rule between layers |
| `Openspace.Domain.Tests` | Office and WorkTask invariants, task status transitions, agent profile validation |
| `Openspace.Application.Tests` | Use cases, queue, task runner, agent signals, briefings and activity, with in-memory fakes |
| `Openspace.Infrastructure.Tests` | SQLite round trips through the real migrations, OpenCode event translation, the OpenCode HTTP client against a fake server, git worktrees on a real throwaway repository |
| `Openspace.Api.Tests` | HTTP endpoints, error mapping and SignalR messages on the real host |

Frontend tests live next to the code (`src/**/*.test.ts`) and run with
`npm test` in `src/frontend`.

```bash
dotnet test
```

Test data is always made up: no real names, companies or credentials.
