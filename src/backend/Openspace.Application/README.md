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

## Status

Empty.
