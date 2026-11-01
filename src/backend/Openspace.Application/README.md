# Openspace.Application

The use cases of Openspace: what a person, an agent or the clock can ask the
building to do. It drives the domain and talks to the outside world only through
ports, which `Openspace.Infrastructure` implements.

## Rules

- Depends only on `Openspace.Domain`; knows nothing of HTTP, SignalR, SQLite,
  OpenCode or Claude Code wire formats.
- Commands load an aggregate, call domain methods and save through `IUnitOfWork`.
  Queries return read-only views (`OfficeView`, `WorkTaskView`…), never domain
  objects.
- A missing office or task throws `NotFoundException`; a broken rule, the domain's
  `DomainException`. The API turns them into 404 and 422.
- The clock is injected as `TimeProvider`, so tests control time.
- What lives only while it happens (activity, energy, interns, guests, why queued
  tasks wait, the graphics card) is kept in memory by singleton boards, which push
  every change through `IActivityPublisher` and answer a snapshot for the page.

## Layout

| Folder | What it holds |
| --- | --- |
| `Offices/` | `OfficeCommands` (offices, rooms, desks, layouts, floor plans, private folders, reserve models), `OfficeQueries` (views and branches) |
| `Roles/` | `RoleCommands` (define, change, delete, draft instructions with AI), `RoleQueries` |
| `Work/` | `WorkTaskCommands` (the life of a task: assign, approve, accept, send back, merge, catch up, pull requests, notes, dependencies), `WorkTaskQueries`, `Supervisor` (splits a request into proposed tasks), `EpicQueries`, `FeedQueries` (the activity feed), `UsageQueries` (tokens per desk), `AuditTrail` and `TaskReport` (the log and its Markdown report) |
| `Queue/` | `QueueDispatcher` (who starts now), `QueueWaits` (why the others wait), `QueueOptions` |
| `Agents/` | `TaskRunner` (worktree, session and brief for each started task), `Briefing`, `AgentSetups` (engine, model, role and private folders of a desk's agent, or of a reserve), `AgentSignals` (what agents report: progress, tools, permissions, the end), `PermissionPolicy` and `PermissionRequests` (auto mode), `LoopGuard` (agents going in circles), `EngineLimits` (accounts over their limits), and the boards `ActivityBoard`, `ContextBoard` (energy), `InternBoard` |
| `Review/` | `TaskReviewer` and `PendingReviews`: Claude reads finished work and leaves a note before a person does |
| `Checks/` | `MergeChecks`: each room's check command on a merged branch |
| `Reports/` | `ReportWriter` (a day, an epic or a changelog, from the audit log, in a chosen language), `ReportFacts`, `ChangelogFacts`, `ReportQueries` |
| `LocalModels/` | `GpuArbiter` (who has the graphics card), `GpuShift` (loads, unloads, gives way), `LocalChat` (routines asking a local model through the arbiter) |
| `Guests/` | `GuestWatcher` and `GuestBoard`: Claude Code sessions started outside Openspace, as guests of the office whose repository they work in |
| `Routines/` | `RoutineRunner` (runs due routines one at a time), `RoutineCommands`, `RoutineQueries`, `ProposalCommands`, `RoutineContext` (log, memory, proposals, delivery), and the kinds in `Kinds/` |
| `Weather/` | `WeatherReport`: the town's weather, read every quarter of an hour |
| `Releases/` | `ReleaseWatch`: the latest published release, compared with the version running |
| `Google/` | The port to the person's Google accounts |
| `Abstractions/` | The ports used across folders |

## The queue

`QueueDispatcher.DispatchAsync` runs every couple of seconds from a single place
(it is not thread-safe by design). Oldest first, it starts as many queued tasks as
there are free seats (`Queue:Seats`, one by default). A task waits while:

- its desk is busy with another task (a desk works on one at a time);
- a task it builds on is not accepted and merged yet;
- its desk's account is over its limits and no reserve model is free;
- it runs on a local model that is not loaded, or is in use, or the graphics card
  is lent to a program that comes first;
- no seat is free.

Each round hands `QueueWaits` the reason of every task that waits, in a sentence
for the board.

## Routine kinds

Each kind implements `IRoutineKind` (`Key`, `Name`, `Description`, default
settings, `RunAsync`) and works through its `RoutineContext`.

| Key | What it does |
| --- | --- |
| `morning-paper` | News from the chosen feeds, judged by a decider and written up by a local model as a numbered PDF paper, with priorities, topics to avoid, section rules and a kanji of the day |
| `mail` | Sorts new mail under labels, archives what can be, proposes replies as drafts, adds dates to the calendar or tasks |
| `newsletters` | A paper of the week's newsletters, by topic |
| `followup` | Mail sent and never answered, with a reminder drafted on a yes; mail waiting for the person |
| `weekly` | A recap of the week: calendar, tasks, proposals waiting, routines' runs |
| `job-offers` | Offers from job boards and companies' own pages, judged against the person's profile, never proposed twice |
| `cv` | The CV in several languages, and one tailored to each offer the person said yes to |
| `maintenance` | Every project's dependencies against advisories and registries, system updates, unpushed work; tasks for the offices' agents |

Settings of each kind are described in the
[API's README](../Openspace.Api/README.md#kinds-of-routine).

## Ports

| Port | Implemented by |
| --- | --- |
| `IOfficeRepository`, `IWorkTaskRepository`, `IRoleRepository`, `IEpicRepository`, `IReportRepository`, `IRoutineRepository`, `IAuditLog`, `IUnitOfWork` | Infrastructure, EF Core on SQLite |
| `IDatabaseBackups` | `SqliteBackups` |
| `IAgentRuntime`, `IAgentEngines` | `AgentRuntimeRouter` (to `OpenCodeRuntime` or `ClaudeCodeRuntime`), `AgentEngines` |
| `IWorkspaceManager` | `GitWorkspaceManager`: one git worktree per task |
| `IPullRequests` | `GitHubPullRequests`, through `gh` |
| `ICheckRunner` | `ShellCheckRunner` |
| `IActivityPublisher`, `IDomainEventPublisher`, `IRoutineProgress` | SignalR in the API; null publishers when there is no hub |
| `ILocalModelServer`, `ILocalModelApi`, `IGpuProbe`, `IDecider` | `LocalModelServer`, `LlamaServerApi`, `GpuProbe`, `DeciderClient` |
| `IClaudeSessions` | `ClaudeSessionLogs`: the tail of Claude Code's own session logs |
| `IGoogleAccounts`, `IMailbox`, `IAgenda` | `GoogleAccounts`, `GmailMailbox`, `GoogleAgenda` |
| `IRoutineOutbox` | `TelegramOutbox` |
| `IFeedReader`, `IWebImages`, `IPdfPrinter`, `IKanjiBook`, `IJobBoard` | `FeedReader`, `WebImages`, `FirefoxPdfPrinter`, `KanjiBook`, `JapanDevBoard` and `CompanyBoards` |
| `IDependencyReader`, `IAdvisories`, `IPackageRegistry`, `IToolchains`, `ISystemUpdates`, `IRepositoryStates` | The maintenance adapters in `Upkeep/` |
| `IWeatherSource` | `OpenMeteo` |
| `IReleaseSource` | `GitHubReleases` |

`AgentSetup` is what decides how an agent behaves in a session: its engine, agent
and model (the desk's, the role's, or the task's reserve), the role's instructions
and permissions, and the office's private folders.
