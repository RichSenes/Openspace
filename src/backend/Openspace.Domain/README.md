# Openspace.Domain

The business model of Openspace: offices and their rooms and desks, roles, tasks
and their life, epics, reports, routines. No dependency on frameworks, databases,
OpenCode or Claude Code.

## Rules

- References nothing but the .NET base library; `Openspace.ArchitectureTests`
  keeps it so.
- No public setters on aggregates: state changes through methods named after what
  they mean (`Accept`, `SendBack`, `PlanFloor`), which check their rules and throw
  `DomainException` when one is broken.
- Time is always passed in (`DateTimeOffset now`): the domain never reads the
  clock, so tests are deterministic.
- Aggregates raise domain events; the infrastructure turns them into audit lines
  and live updates after a successful save.

## Layout

```
Common/    AggregateRoot, IDomainEvent, DomainException, Guard
Offices/   Office (aggregate) > Room > Desk; AgentProfile, AgentEngine, DeskLook,
           RoomLayout, FloorPlan, ids and events
Roles/     Role (aggregate), RolePermissions, PermissionLevel
Work/      WorkTask (aggregate), Epic (aggregate), TaskNote, NoteAuthor, AgentRun,
           ProcedureStep, ManualCheck, TokenCount, RoundUsage, AuditEntry
Reports/   Report (aggregate)
Routines/  Routine (aggregate), RoutineSchedule, RoutineRun, Proposal, SeenItem
```

## Office

A project. It holds its rooms and their desks, and says how agents may treat it.

- **Repository**: the git repository its tasks work on.
- **Theme**: the mood the building is drawn in (`cozy`, `neonDistrict`…); each
  room may have its own.
- **Private folders**: up to 20 absolute folders no agent of the office may open,
  read, search or change.
- **Local only**: the project never leaves this computer: no push, no pull
  request, no reserve models in the cloud.
- **Reserve models**: up to four, in order: OpenCode's `provider/model`, or Claude
  Code as `claude-code/` and one of its models. They go on with tasks while a
  desk's account is over its limits.
- **Floor plan** (optional): the building drawn by hand, 6 to 120 tiles wide and
  5 to 80 deep, with rooms placed in it, never overlapping, up to 12 cuts that
  give it its shape, and up to 120 pieces of furniture on the common floor.

### Room

- Names are unique within an office (case-insensitive, trimmed).
- **Kind**: `Work` (desks with agents), `Routines` (the routines' room) or
  `Guests` (Claude Code sessions started outside Openspace; it takes no desks,
  and opens with the first guest).
- **Context**: free text given to every agent working there.
- **Procedure**: up to 20 steps every task of the room must report as done, with
  evidence, before it can go to review.
- **Check**: a command that tells whether the room's code works (`npm test`), run
  after merges.
- **Layout** (optional): its size, where its desks and furniture stand, its door
  and its windows, arranged by hand (`RoomLayout`).

### Desk

- A name unique within its room, free text.
- An `AgentProfile`: the engine (`OpenCode` or `ClaudeCode`), the agent name
  (`lowercase-with-dashes`, OpenCode's agent) and an optional model
  (`provider/model` for OpenCode, `sonnet` or a full Claude model name for Claude
  Code).
- An optional role, and a look (`DeskLook`: skin, hair, hairstyle, clothes,
  accessory, outfit or costume).

## Role

A reusable kind of agent: a unique name, a one-line description, instructions
(the agent's system instructions in every session), an optional preferred model,
and permissions for changing files, running commands, using the web and calling in
helpers, each `Allow`, `Ask` or `Deny`. A role cannot be deleted while desks use it.
It may also name the skills its agents load (`UseSkills`): left unnamed, they get every
installed skill; an empty list gives them none.

## WorkTask

A task may carry an output folder (`WriteOutputTo`, a full path, set before its agent
starts): somewhere outside its copy the agent may also write in, such as a security
audit's run folder.

"Task" in the glossary; `WorkTask` in code, so it never clashes with
`System.Threading.Tasks.Task`. Its own aggregate, referencing its office, desk and
epic by id.

```
Proposed ──approve──▶ Queued ──start──▶ Running ──submit──▶ Review ──accept──▶ Done
                        ▲                  │ │                 │
                        ├────requeue───────┘ └──fail──▶ Failed │
                        ├────retry───────────────────────┘     │
                        └────send back (a new round)───────────┘

Any open status ──discard──▶ Discarded
```

- `Propose` creates a task waiting for approval, possibly without a desk; `Assign`
  creates it for a desk and queues it at once. The desk can change only while the
  task is proposed, queued or failed, and must belong to its office.
- **Dependencies**: up to 10 tasks of the same office it waits for; it starts only
  once each is accepted and merged. Loops are refused.
- **Rounds**: every send-back starts a new round; tokens are counted per round.
- **Procedure**: when it first starts, the task copies its room's procedure as a
  numbered checklist; the agent completes steps with evidence. An agent stopping
  with steps missing is reminded a few times, then the task fails.
- **Manual checks**: what a person must try by hand before merging, ticked when
  done.
- **Reserve model**: set while the desk's account is over its limits and a
  reserve goes on with the task; cleared once the desk takes it back.
- **Merge and pull request**: where accepted work was merged, or the pull request
  it went to. A conflict can reopen it as a new round to catch up with the target.
- **Notes** are written by a person, by the desk's agent or by the system (the
  reason of a failure, a reviewer's note).

## Epic

A larger request split into tasks by the supervisor: a title, the request in the
words of whoever asked, who and when. Tasks belong to at most one epic of their
own office.

## Report

A written account, kept as written: of a period of an office (usually a day), of
an epic, or a changelog. Its body is Markdown; `WrittenByAi` is false when the
model was unavailable and the report lists the recorded facts only.

## AuditEntry

One line of a task's audit log: when, what kind (`Lifecycle`, `Step`, `Read`,
`Change`, `Command`, `Search`, `Permission`, `Note`, `Other`), a one-sentence
summary, the evidence (a diff, a command's output; cut beyond 16,000 characters),
whether it worked, and its source event. Lines are only ever added.

## Routines

- **Routine**: a recurring job of an office: a kind (`morning-paper`, `mail`…),
  settings as JSON, a schedule, on or off, and whether every proposal waits for a
  yes ("ask first").
- **RoutineSchedule**: a time on the person's clock, some days of the week (none
  means every day), every N weeks from a start date.
- **RoutineRun**: one run, with its log and how it ended (`Running`, `Succeeded`,
  `Failed`).
- **Proposal**: something a routine wants to do with the person's things (file a
  mail, add a reminder, save a CV): `Pending` until a yes (`Approved`, then `Done`
  or `Failed`) or a no (`Rejected`).
- **SeenItem**: what a routine already met (a news item, an offer, a mail), by a
  key and a fingerprint. It comes back only if it changed in substance, and never
  once dismissed (`Skipped`, `Shown`, `Dismissed`, `Taken`).
