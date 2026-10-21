# Openspace.Domain

The heart of Openspace: the business model of the building, with no dependency
on frameworks, databases or OpenCode.

## Responsibilities

- Aggregates and entities: `Office`, `Room`, `Desk`, `Task` (see the glossary in
  [docs/vision.md](../../../docs/vision.md)).
- Invariants and state transitions, e.g. which task statuses can follow which.
- Domain events raised when something meaningful happens (a task starts, a desk
  falls asleep).

## Rules

- References nothing but the .NET base library. Enforced by
  `Openspace.ArchitectureTests`.
- No public setters on aggregates: state changes through intention-revealing
  methods.

## Model

```
Common/   AggregateRoot, IDomainEvent, DomainException
Offices/  Office (aggregate) > Room > Desk, AgentProfile
Roles/    Role (aggregate), RolePermissions, PermissionLevel
Work/     WorkTask (aggregate), Epic (aggregate), TaskNote, NoteAuthor, AgentRun, AuditEntry
```

### Office

An aggregate holding its rooms and their desks.

- Room names are unique within an office, desk names within a room
  (case-insensitive, trimmed). Desks can be renamed; their name is free text,
  separate from the OpenCode agent that runs them.
- A room has free-text **context**, injected into every agent working there,
  and an optional **procedure**: up to 20 steps every task of the room must follow.
- A desk is bound to an `AgentProfile`: the OpenCode agent name
  (`lowercase-with-dashes`) and an optional model override (`provider/model`).
- Events: `RoomOpened`, `DeskAdded`.

### Role

A reusable kind of agent: name (unique), one-line description, instructions
(given to the agent as system instructions in every session), optional preferred
model, and permissions for changing files, running commands and using the web,
each `Allow`, `Ask` or `Deny`. A desk may reference a role; a desk without one
runs its OpenCode agent as it is. Events: `RoleDefined`, `RoleChanged`,
`DeskRoleChanged`.

### WorkTask

"Task" in the glossary; named `WorkTask` in code to avoid clashing with
`System.Threading.Tasks.Task`. It is its own aggregate and references its office
and desk by id.

```
Proposed ──approve──▶ Queued ──start──▶ Running ──submit──▶ Review ──accept──▶ Done
                        ▲                  │ │                 │
                        ├────requeue───────┘ └──fail──▶ Failed │
                        ├────requeue─────────────────────┘     │
                        └────send back (with feedback)─────────┘

Any open status ──discard──▶ Discarded
```

- `Propose` creates a task waiting for approval, possibly without a desk.
  `Assign` creates it for a desk and queues it at once.
- A task needs a desk to be queued, and can change desk only while
  proposed, queued or failed. The desk must belong to the task's office.
- `Fail` stores the reason as a system note; `SendBack` stores the reviewer's
  feedback as a note.
- Notes can be written by a human, by a desk (the agent) or by the system.
- When a task first starts it adopts its room's procedure as a numbered
  checklist, so later changes to the room do not affect it. The agent completes
  steps with evidence; a task with missing steps cannot go to review. Reminders
  are counted per run.
- Events: `WorkTaskCreated`, `WorkTaskStatusChanged`, `WorkTaskNoteAdded`.

### Epic

A larger request split into tasks: its title, the request in the words of whoever
asked, who and when. Tasks may belong to one epic of their own office. Event:
`EpicOpened`.

### AuditEntry

One line of a task's audit log: when, what kind (`Lifecycle`, `Step`, `Read`,
`Change`, `Command`, `Search`, `Permission`, `Note`, `Other`), a one-sentence
summary, the evidence (diff, command output, error; cut beyond 16,000
characters), whether it worked, and the id of its source event. Entries are only
ever added.

Time is always passed in (`DateTimeOffset now`): the domain never reads the
clock, which keeps it deterministic in tests.
