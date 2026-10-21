# Openspace

A pixel-art control room for [OpenCode](https://opencode.ai) agents.

Every project is an office, every area of competence is a room, every agent sits
at a desk. Walk through the building, see who is working and what they are doing,
assign tasks, review changes and read reports, instead of scrolling a terminal.

> **Status:** early. Offices, rooms, desks and tasks work end to end with live updates; agents are not connected yet.

## Concepts

| Building | Office | Room | Desk |
| --- | --- | --- | --- |
| The whole instance | A project | An area of competence | An OpenCode agent with a role |

The full picture, glossary and first-prototype scope are in
[docs/vision.md](docs/vision.md).

## Repository layout

```
docs/                          Vision and design notes
src/backend/
  Openspace.Domain/            Business model, no dependencies
  Openspace.Application/       Use cases and ports
  Openspace.Infrastructure/    OpenCode, MCP, git and SQLite adapters
  Openspace.Api/               ASP.NET Core host
src/frontend/                  React + PixiJS building
tests/                         Architecture and domain tests
```

Each project has its own README with detailed specifications.

## Ground rules

- Code, comments and docs are in English.
- No personal or company data: examples and tests use made-up names.
- No keys or secrets in the repository: configuration comes from environment
  variables or ignored local files.

## Requirements

- [OpenCode](https://opencode.ai) running in server mode (`opencode serve`)
- .NET SDK 10
- Node.js 22 or later

## Getting started

```bash
dotnet build && dotnet test
dotnet run --project src/backend/Openspace.Api     # API on http://localhost:5145
```

In a second terminal:

```bash
cd src/frontend
npm install
npm run dev                                         # open http://localhost:5173
```
