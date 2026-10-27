# Openspace.Application

Use cases of Openspace: what a person (or an agent) can ask the building to do.

## Responsibilities

- Commands and queries such as *create a task*, *assign it to a desk*,
  *accept the result*.
- The queue and seat allocation policy.
- Ports (interfaces) for everything outside the process: the OpenCode server,
  git, persistence, live notifications. Implementations live in
  `Openspace.Infrastructure`.

## Rules

- Depends only on `Openspace.Domain`.
- No knowledge of HTTP, SignalR, SQLite or OpenCode wire formats.

## Use cases

| Class | What it does |
| --- | --- |
| `OfficeCommands` | Open an office, open a room, set a room's context, add a desk |
| `OfficeQueries` | List offices, get one with its rooms and desks |
| `WorkTaskCommands` | Assign, propose, approve (optionally choosing the desk), accept, send back, retry, discard, add a note |
| `WorkTaskQueries` | List tasks by office and status, oldest first; get one with its notes |
| `RoutineRunner`, `RoutineCommands`, `RoutineQueries`, `ProposalCommands` | Routines: run due ones one at a time (by hand first), create and change them, list them with their runs; a yes or a no to what they propose. Kinds implement `IRoutineKind` and work through a `RoutineContext` (log, memory of what was met, proposals, delivery) |
| `LoopGuard` | Notices agents going in circles (the same failure again and again, a conversation filling up) and gives them a fresh start, then stops them for review |
| `QueueDispatcher` | Hands free seats to queued tasks (see below); a task on a local model waits until `GpuArbiter` admits it |
| `GpuArbiter` + `GpuShift` | Who has the graphics card: programs listed first, or claiming it, before local models; loads the model desks wait for, unloads it when idle, sends local tasks back to the queue when a program needs the card |
| `Supervisor` | Splits a request into proposed tasks for the office's desks. Desk names are the only allowed values in the schema; since OpenCode does not enforce schemas strictly, tasks that still lack a valid desk get a short follow-up question (one desk per task). With a single desk everything goes to it; a task left without one says what was suggested (the model reads the repository, read-only, and answers with JSON matching a schema); approves all proposals of an epic at once |
| `EpicQueries` | Epics of an office, newest first |
| `ReportWriter` | Builds the facts of a day or an epic from the audit log (`ReportFacts`), asks the model to write them up for people in a chosen language, falls back to the facts when it cannot, and keeps the report |
| `ReportQueries` | Reports of an office, one report |
| `RoleCommands` | Define, change and delete roles (not while desks use them); draft instructions with AI |
| `RoleQueries` | List roles |
| `TaskRunner` | Gives started tasks a worktree, a session and a brief; builds the `AgentSetup` from the desk and its role |
| `AgentSignals` | Reacts to what agents report (progress, tools, permissions, finish, failure) and to the Openspace tools. An agent that stops with procedure steps missing is reminded twice, then the task fails |
| `AgentSetups` | Builds the session setup of a desk's agent from the desk and its role |

Commands load an aggregate, call one domain method and save. Queries return
read-only views (`OfficeView`, `WorkTaskView`), never domain objects.

Errors: a missing office or task throws `NotFoundException`; a broken rule
throws the domain's `DomainException`.

## Queue

`QueueDispatcher.DispatchAsync` starts as many queued tasks as there are free
seats (`Queue:Seats` in configuration, default 1), oldest first. A desk works on
one task at a time: if its desk is busy, a task waits even when other seats are
free, and the next task in line goes ahead. It returns the started tasks, which
will be handed to OpenCode.

It is not thread-safe by design: a single queue runner will call it.

## Ports

| Interface | Implemented in |
| --- | --- |
| `IOfficeRepository`, `IWorkTaskRepository` | Infrastructure (EF Core) |
| `IUnitOfWork` | Infrastructure (`OpenspaceDbContext`) |
| `IRoleRepository`, `IAuditLog` | Infrastructure (EF Core) |
| `IAgentRuntime` | Infrastructure (`OpenCodeRuntime`) |
| `IWorkspaceManager` | Infrastructure (`GitWorkspaceManager`) |

`AgentSetup` is what decides how an agent behaves in a session: the desk's
OpenCode agent, the desk's model or else the role's, the role's instructions and
the role's permissions.

The clock is injected as `TimeProvider`, so tests control time.

Domain events are collected on aggregates but not dispatched yet: that comes with
live updates.
