# Openspace.Infrastructure

The adapters that connect Openspace to the outside world: the agent engines, git,
the database, Google, Telegram, the graphics card, public web services. Each one
implements a port of `Openspace.Application` (see the table there).

## Rules

- Implements interfaces declared in `Openspace.Application`; never referenced by
  `Openspace.Domain` or `Openspace.Application`.
- No secrets here or in the database: OpenCode and Claude Code keep their own
  sign-ins; Google keys and Telegram tokens are files in the person's own folder
  (`~/.config/openspace`), readable by them only.
- Everything that reaches a real service has a fake in the tests.

## Adapters

| Folder | What it does |
| --- | --- |
| `OpenCode/` | `OpenCodeRuntime` (HTTP client for `opencode serve`), `OpenCodeEventListener` and `OpenCodeEventTranslator` (its event stream to agent signals), `OpenCodeStatusSweep` (sessions already retrying), `OpenCodeContextWindows` (how big each model's conversation can be) |
| `ClaudeCode/` | `ClaudeCodeRuntime` (`claude -p` in the task's working copy, following its stream, resuming the same conversation), `ClaudeCodeEvents`, `ClaudeCodePermissions` (the role's tools, the rest asked through Openspace), `ClaudeCodeUsage` (the account's limits), `AgentRuntimeRouter` (each session to its engine) |
| `Git/` | `GitWorkspaceManager` (a worktree per task, diffs, commits, merges), `GitCli`, `GitHubPullRequests` (through `gh`) |
| `Checks/` | `ShellCheckRunner`: a room's check command in a throwaway copy, with a timeout |
| `Persistence/` | EF Core on SQLite: the repositories, the audit log, migrations, `SqliteBackups` |
| `LocalModels/` | `LocalModelServer` (starts and stops the model's server), `LlamaServerApi`, `GpuProbe` (graphics memory and running programs), `OpenCodeLocalProvider` (the provider OpenCode reads), `DeciderClient` (yes/no questions on the processor) |
| `Guests/` | `ClaudeSessionLogs`: the end of Claude Code's own session logs; only the folder, branch, time, how it was started and tool names are kept |
| `Google/` | `GoogleAccounts` (OAuth on this computer), `GmailMailbox` (read, label, archive, draft; never send), `GoogleAgenda` (calendar and tasks) |
| `Telegram/` | `TelegramOutbox`: what routines make, to the person's group |
| `Routines/` | `FeedReader` (RSS and Atom, no DTDs), `WebImages`, `FirefoxPdfPrinter` (HTML to PDF with a headless Firefox), `KanjiBook`, `JapanDevBoard` and `CompanyBoards` (job offers) |
| `Upkeep/` | Dependency files of every kind, OSV advisories, package registries, toolchains, system packages, unpushed work |
| `Weather/` | `OpenMeteo`: town lookup and current weather, no key |
| `Releases/` | `GitHubReleases`: the latest release on GitHub's public API, no key |
| `Skills/` | `SkillCatalog`: the installed skills from their SKILL.md, and a plugin of links per set of skills for Claude Code desks |
| `Security/` | `SecurityAuditRecords`: run folders, `findings.json`, `run-metadata.json` and `REPORT.md` of security audits, read as they are written |
| `Hosting/` | The background services: queue, reviews, routines, graphics card, guests, backups, releases |

### What leaves the computer

Only what each feature needs: prompts to the model providers you chose in
OpenCode or Claude Code; the town's name and position to Open-Meteo; package names
to OSV and the registries; feed and job board requests; Google and Telegram calls
of your own accounts; GitHub through `gh` when you push or open a pull request,
and the release check. Requests that are not yours go out under an honest name
(`Openspace/1.0`).

## Persistence

EF Core on SQLite, one file (`ConnectionStrings:Openspace`).

| Table | Content |
| --- | --- |
| `Offices`, `Rooms`, `Desks` | Offices with their rooms and desks (agent, role, look, spot) |
| `Furniture`, `CommonFurniture`, `FloorCuts` | What stands in rooms and on the common floor, and the building's shape |
| `Roles` | Roles, with their permissions as text columns |
| `WorkTasks`, `TaskNotes`, `TaskProcedureSteps`, `TaskManualChecks`, `TaskRoundUsage` | Tasks with their notes, procedure, checks to try by hand and tokens per round |
| `AuditEntries` | The audit log: one row per thing that happened, numbered in recording order |
| `Epics`, `Reports` | Requests split by the supervisor, and the reports written |
| `Routines`, `RoutineRuns`, `Proposals`, `SeenItems` | Routines, their runs, what they propose and what they already met |

- Rooms, desks, notes and the rest are owned entities, always loaded and saved
  with their aggregate.
- Strongly typed ids are stored as GUIDs; enums as text; `DateTimeOffset` as a
  sortable 64-bit number, because SQLite cannot compare dates natively.
- The schema evolves through migrations in `Persistence/Migrations`; the API
  applies them at startup. A copy of the database is taken every day
  (`Backups:*`).

### Adding a migration

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/backend/Openspace.Infrastructure --output-dir Persistence/Migrations
```

## OpenCode

Openspace talks to one `opencode serve`, using its long-standing endpoints:
`POST /session`, `POST /session/{id}/prompt_async`, `POST /session/{id}/abort`,
`GET /session/{id}/message`, `GET /agent`, `POST /permission/{id}/reply`, `POST /mcp` and the
server-sent events of `GET /global/event`. Every call passes `?directory=` with
the task's worktree, so a single server serves every task.

Roles need no OpenCode configuration: their permissions become the session's
rules at creation (`edit`, `bash` and `webfetch`, each `allow`, `ask` or `deny`
for every pattern) and their instructions travel as `system` with every prompt.
Every working session also gets `external_directory: ask`, so an agent never
reads or works outside its own worktree without a person agreeing; read-only
sessions (supervisor, drafts) get `deny`.
Supervisor plans run in the office repository in the same kind of read-only
`plan` session, with `format: { type: "json_schema", schema, retryCount: 2 }`;
the answer is read from the message's `info.structured`.

Drafts run in an empty scratch folder, in a `plan` session that may not edit,
run commands or use the web, answered synchronously with
`POST /session/{id}/message`; the session is deleted afterwards.

Events become speech bubbles like this:

| OpenCode event | Becomes |
| --- | --- |
| `todo.updated` | The item `in_progress`, in the agent's words: the bubble headline |
| `message.part.updated`, tool running | A short phrase: `Editing header.php`, `Running npm test`, `Searching the code` |
| `permission.asked` | The desk waits for you, with Allow once / Refuse. OpenCode then reports the blocked tool as running: that does not end the wait |
| `permission.replied` | That wait ends, also when the answer came from OpenCode itself |

An agent can ask several things at once, but a desk shows one request at a time:
after each answer Openspace reads `GET /permission?directory=` and shows the next
request of the same session, if any. OpenCode is the source of truth for what is
still waiting.
| `session.idle` | The task goes to review; the agent's last answer becomes a note |
| `session.error` | The task fails with the reason (except aborts Openspace asked for) |
| `message.part.updated`, tool completed or error | An audit log line: `Changed header.php` with the diff, `Ran npm test` with the command, its output and whether it exited 0, `Read index.html`, `Searched the code for …` |

When a task resumes (after a restart or a send-back), pending permissions of its
session are read back with `GET /permission?directory=`, since OpenCode does not
ask twice.

Calls to Openspace's own tools (`openspace_*`) are skipped here: their effect
(a note, a step) is what gets logged.

Audit lines from tool calls carry the OpenCode call id; a unique index on
(task, call id) records each call once even if OpenCode repeats the event. Lines
about the task's own life (created, started, merged, notes…) come from domain
events and are written in the same `SaveChanges` as the change they describe.

## Workspaces

Each task gets `git worktree add -b openspace/<title>-<key>` from the repository's
`HEAD`, under `Workspaces:Root`. Your checkout and branch are never touched.

- Check: the repository folder must exist, be inside a git work tree and have a
  `HEAD`. Initialize fixes the last two with `git init` and an empty first commit.
- Diff: `git add --intent-to-add --all` then `git diff HEAD`, so new files show.
- Accept: commit on the task branch (with the repository's identity, or
  `Openspace` if none), then remove the worktree. The branch stays for you to merge.
- Merge: into the checked-out branch only when it has no uncommitted changes;
  into any other branch inside a temporary worktree, so your checkout never moves.
  Always `--no-ff` with message `Merge task: <title>`. A conflict is aborted and
  nothing changes. Afterwards the task branch is deleted, once git confirms it is
  contained in the target.
- Discard: remove worktree and branch.

## Claude Code

Each prompt runs `claude -p` in the task's working copy with `--restricted`: the
person's own Claude Code settings never apply, only the role's tools. Reading is
always allowed; editing, commands and the web follow the role. Whatever the role
does not allow outright goes through the `permission` tool of Openspace's MCP
server and waits for the person. Its stream becomes the same agent signals as
OpenCode's; later prompts resume the same conversation. It uses the account
Claude Code is signed in with, and that account's limits.
