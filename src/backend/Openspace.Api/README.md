# Openspace.Api

ASP.NET Core host. Composes the layers and exposes them to the frontend.

## Endpoints

| Method | Path | Description |
| --- | --- | --- |
| GET | `/health` | Liveness check, returns `{ "status": "ok" }` |

Live updates for the building (bubbles, desk states, queue) will be pushed with
SignalR.

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

`appsettings.json` holds non-secret defaults. Local overrides go in
`appsettings.Local.json` (ignored by git) or environment variables, for example
`Queue__Seats=2`.
