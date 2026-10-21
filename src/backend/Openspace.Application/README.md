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
| `QueueDispatcher` | Hands free seats to queued tasks (see below) |

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

The clock is injected as `TimeProvider`, so tests control time.

Domain events are collected on aggregates but not dispatched yet: that comes with
live updates.
