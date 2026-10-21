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

## Configuration

`appsettings.json` holds non-secret defaults. Local overrides go in
`appsettings.Local.json` or environment variables, both ignored by git.
