# Tests

| Project | Covers |
| --- | --- |
| `Openspace.ArchitectureTests` | Clean Architecture dependency rule between layers |
| `Openspace.Domain.Tests` | Office and WorkTask invariants, task status transitions, agent profile validation |
| `Openspace.Application.Tests` | Use cases and queue rules, with in-memory fakes |
| `Openspace.Infrastructure.Tests` | Round trips through a real in-memory SQLite database built from the migrations |

```bash
dotnet test
```

Test data is always made up: no real names, companies or credentials.
