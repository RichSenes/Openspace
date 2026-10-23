# Openspace

A pixel-art control room for [OpenCode](https://opencode.ai) and
[Claude Code](https://claude.com/claude-code) agents.

Every project is an office, every area of competence is a room, every agent sits
at a desk. Walk through the building, see who is working and what they are doing,
assign tasks, review changes and read reports, instead of scrolling a terminal.

> **Status:** early but working. Agents run through OpenCode or Claude Code in their own git worktrees, with live speech bubbles, a review by Claude before yours, diffs, and commits on task branches.

## Concepts

| Building | Office | Room | Desk | Role |
| --- | --- | --- | --- | --- |
| The whole instance | A project | An area of competence | A seat for an agent, on OpenCode or Claude Code | How that agent works and what it may do alone |

The full picture, glossary and first-prototype scope are in
[docs/vision.md](docs/vision.md).

## Repository layout

```
docs/                          Vision, design notes and the sprite format (sprites.md)
src/backend/
  Openspace.Domain/            Business model, no dependencies
  Openspace.Application/       Use cases and ports
  Openspace.Infrastructure/    OpenCode, MCP, git and SQLite adapters
  Openspace.Api/               ASP.NET Core host
src/frontend/                  React + PixiJS building
tests/                         Architecture and domain tests
tools/                         Helper scripts (pixelgrid.py: pictures to fan pack sprites)
```

Each project has its own README with detailed specifications.

## How a task flows

1. You create a task for a desk, or ask the supervisor: it reads the project and
   splits a larger request into tasks proposed for the right desks, which you
   approve.
2. When a seat is free, Openspace creates a git worktree on a new
   `openspace/...` branch and opens an OpenCode session there, briefing the agent
   with the room's rules, the task and the team's notes.
3. While it works, the agent's todo list and tool calls become speech bubbles.
   Through Openspace's own MCP tools it can also leave notes on the task,
   announce what it is doing and re-read its brief.
   If it asks for a permission, Openspace answers on its own in auto mode
   and the desk waits for your answer only for risky actions, such as
   pushing, deploying or touching secrets.
4. When it stops, the task goes to review with the agent's summary as a note,
   the diff of its changes, the checks to try by hand and the tokens it spent.
   Claude Code reviews it first when it is there.
5. Accept commits the work on the task branch. Send back resumes the same
   session with your feedback, the review, or both: a new round. Discard
   throws it all away.
6. If the room has a required procedure (for example "back up every file before
   changing it"), the agent reports each step with evidence, and the work does
   not reach review until all of them are done.
7. Every step, file change, command (with its output), permission and note is
   kept in the task's audit log, and can be downloaded as a Markdown report.
8. Reports of a day or of an epic are written from the audit log in the
   language you choose, ready to copy into an email or a chat.
9. Accepted work can then be merged into the branch you choose, from the task
   itself. Your checkout never moves, and nothing is touched if you have
   uncommitted changes or the work conflicts.

## Ground rules

- Code, comments and docs are in English.
- No personal or company data: examples and tests use made-up names.
- No keys or secrets in the repository: configuration comes from environment
  variables or ignored local files.

## Requirements

- [OpenCode](https://opencode.ai) running in server mode (`opencode serve`)
- Optionally [Claude Code](https://claude.com/claude-code), signed in: when it is
  installed it plans, drafts and reviews finished work, and desks can run on it.
  It uses the account it is signed in with and that account's limits; Openspace
  never needs an API key for it
- .NET SDK 10
- Node.js 22 or later

## Getting started

Start OpenCode in server mode (any folder will do: Openspace tells it which
folder each task works in):

```bash
opencode serve --port 4096
```

Then Openspace:

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
