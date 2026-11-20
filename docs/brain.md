# Shiori: the shared brain

The plan for Openspace's second brain, called Shiori (栞, a bookmark: it keeps your place and
shows the way). It covers why it exists, what is already in Openspace to build on, how it is
designed, and the phases to get there. The work split into user stories is in
[brain-stories.md](brain-stories.md).

Status: draft to discuss. Nothing below is built yet; the founding rules in
[Development rules](#development-rules) are proposals to decide together.

## Why

Openspace runs many agents on the same projects. Today they share only the room's context, the
notes of their own task and the list of merged tasks theirs builds on. Nobody tells an agent
what the desk next door is changing right now, nor why something was decided the way it was.
The result, more often than expected:

- two desks change the same files in parallel and the merge conflicts, even with a supervisor
  and separate roles;
- an agent undoes a choice made weeks ago because it never saw the reasons;
- studies, plans and decisions made with the person live in chats and get lost.

Shiori is one place where all of this is written, read before acting, and kept: for the agents,
for the person, and for whoever downloads Openspace, who gets the same experience out of the box.

## Goals

1. **One brain per installation**, organised the same way for everyone, in plain Markdown that
   Obsidian opens as a vault.
2. **Agents read before they act and write what they did**: decisions and rules come into every
   briefing; each agent keeps a journal; Openspace keeps a live list of who is changing what.
3. **Fewer conflicts**: work that would touch the same files is told, warned about, or put in order.
4. **Nothing lost**: decisions with their reasons and review date, plans, studies, session notes,
   weekly and monthly digests.
5. **A way to read it** (the wiki and the map) and **a way to ask it** (a chat that answers from
   the brain and cites where it found the answer).
6. **Shiori grows into the supervisor**: brainstorm with it, decide, then let it organise work
   across every office. Later, by voice.
7. Later still, **inhabitants**: agents with a job outside code (examples: a secretary for email,
   a coach, an updates keeper), shared agenda, agreements between them under the person's rules,
   and personalities that colour how they talk, never what they deliver.

## Non-goals

- Shipping anyone's content: the repository ships the mechanism and an empty, well organised
  vault; what a person writes stays on their machine.
- Replacing Obsidian: the vault stays a normal folder; Obsidian is optional.
- Letting personality change quality, or any agent act against the person.

## Principles

1. **Files are the truth.** The vault is Markdown with YAML front matter. Openspace indexes it,
   but deleting the index loses nothing.
2. **Mechanism public, content private.** The vault lives outside the repository, under the
   person's data folder, and is never pushed anywhere by Openspace.
3. **Everyone writes in their own area; the common area changes through proposals.** An agent
   writes its journal freely; a decision, a rule or a shared note is proposed and approved (by the
   person, or by Shiori when the constitution allows it).
4. **Read before acting.** A briefing carries what applies to the task: rules, decisions on its
   files and room, who else is working nearby.
5. **Every answer says where it comes from.** Shiori cites notes; a decision links the task or the
   session it came from.
6. **Every function has its model**, chosen in Settings: Shiori (chat and supervisor) on the
   latest Opus by default; lighter or local models for digests, journals and the simulation.
7. **One voice**: the person talks to Shiori, and Shiori deals with the inhabitants.
8. **Role, traits, look and theme are separate choices.** "Developer" is a role; "solitary" or
   "sunny" are traits; ready-made bundles are only suggestions that can be taken apart.

## Vocabulary

| Word | Meaning |
|---|---|
| **Shiori** | The brain: the vault, its index, and the agent that answers from it and supervises. |
| **Vault** | The folder of Markdown notes. |
| **Constitution** | The person's rules, numbered. They win over everything else. |
| **Decision** | A choice with its reasons, scope (offices, rooms, file paths), status and review date. |
| **Journal** | An agent's own notes: what it touched, learnt, left half done. |
| **Now** | The live list, written by Openspace, of who is changing which files. |
| **Session** | A conversation with Shiori, with its whiteboard. |
| **Digest** | The weekly or monthly summary, written into the vault. |
| **Inhabitant** | Any agent with an identity: today's desks, tomorrow's personal agents. |
| **Role / traits / look / theme** | What it does / how it is / how it appears / which world it lives in. |

## What Openspace already has

| Piece | Today | Use for Shiori |
|---|---|---|
| `Briefing` | Room context and rules, the task, team notes, merged tasks it builds on | Add the constitution, the decisions in scope, *Now* overlaps, the room's expert notes |
| MCP tools (`get_task`, `add_task_note`, `announce`, `complete_step`, `wait_for_person`…) | Agents talk to Openspace while they work | Add the brain's tools on the same server |
| Task notes and audit log | Per task, in the database | Source for journals, decisions' links, digests |
| `Supervisor` + supervisor jobs | Plans one office's request as tasks to approve | Becomes the "organise" step of a Shiori session, across offices |
| Claude Code runtime | Sessions that resume (`--resume`), roles, MCP | Shiori's chat runs as a resumable Claude Code session on Opus |
| Routines, proposals, Telegram | Scheduled jobs, yes/no, delivery | Digests, decision reviews, "tomorrow morning", the chronicle in the paper |
| Settings file + reload | Settings applied without restart | Models per function, vault folder |
| Local models, decider, GPU arbiter | Gemma and a small CPU model, shared fairly | Journals, chronicle and simulation flavour at no quota cost |
| Needs-you inbox | Everything waiting for the person | Decision proposals, reviews due, agreements that need the person |
| Office and routine rooms in the building | Pixel art with desks, interns, guests, routines | The brain's place in each theme, inhabitants consulting it |

The supervisor today is one-shot per office, the briefing cannot see other tasks in flight, and
nothing durable records why. Those are the gaps.

## Design

### The vault

Default folder: `~/.local/share/openspace/brain` (Settings › Brain can move it, for example into
a folder the person syncs). Openspace creates it on first start with this structure and a
README note in each folder that explains what goes there:

```
Constitution.md            the person's rules, numbered; agents cite them as "rule 4"
Now.md                     written by Openspace only: who is changing what, right now
Decisions/                 2026-11-20-merge-order.md …
Plans/                     longer plans, with their phases
Studies/                   research: libraries, projects, options compared
Sessions/                  conversations with Shiori and their whiteboards
Digests/                   weekly and monthly summaries
Offices/<office>/          overview, conventions, glossary of that project
Inhabitants/<name>/        profile.md (role, traits, look), journal.md
Chronicle/                 social events, for the paper and the wiki
```

Front matter, the same for every kind (fields that do not apply are left out):

```yaml
kind: decision            # rule | decision | plan | study | session | digest | journal | note
status: accepted          # proposed | accepted | superseded | rejected
offices: [Shop]           # scope; empty means everywhere
rooms: [Web]
paths: ["src/web/**"]     # files the note is about
review: 2027-02-20        # when to look at it again
supersedes: "[[2026-09-01-merge-order]]"
from: "task 01a1…"        # or a session, a study
author: Screens           # an inhabitant, Shiori, or the person
```

Links are Obsidian wikilinks (`[[note]]`). The vault is a **local git repository**: every change
is a commit with its author, which gives history, diffs and undo in the wiki for free.
Openspace never adds a remote to it.

### Index and search

The files are read into an SQLite FTS5 table in Openspace's database, rebuilt on start and kept
current by a file watcher, so notes edited in Obsidian are seen at once. Search ranks by text,
then by scope (same office, room, paths), recency and status (accepted before proposed,
superseded last). Semantic search with a local embedding model can come later behind the same
interface.

### Who writes what

| Area | Agents | Shiori | The person |
|---|---|---|---|
| Own journal | write | read | read, edit |
| Decisions, plans, studies, office notes | propose | propose; accept when the constitution allows | accept, edit |
| Constitution | read | propose | the only one who changes it |
| Now | read | read | read (Openspace writes it) |
| Sessions, digests, chronicle | read | write | read, edit |

A proposal is a note with `status: proposed`; it appears in the needs-you inbox with
"Accept", "Change" and "Reject".

### Now: who is changing what

Openspace already knows each running task's working copy. Every few seconds it reads the files
changed there (and the files a plan said it would touch) and writes `Now.md`. With it:

- the briefing of a task lists the tasks in flight on the same files or room;
- the dispatcher can **hold** a queued task whose files overlap a running one, saying why
  (as it already says why tasks wait), instead of letting both run into a conflict;
- before a merge, overlapping branches are flagged.

### Agents and the brain

New MCP tools on the existing server:

| Tool | What it does |
|---|---|
| `brain_search(query, scope?)` | Notes that match, with their status and a short excerpt |
| `brain_read(note)` | One note |
| `brain_journal(text)` | Appends to the agent's journal |
| `brain_propose(kind, title, body, scope)` | A decision or note proposal for the person or Shiori |
| `brain_now()` | Who is working on what, now |

The briefing grows a "Before you start" part: the constitution's rules, the accepted decisions
whose scope matches the task's office, room or paths, overlaps from *Now*, and the room's expert
notes. At the end of a task the agent is asked to write its journal entry and, when it made a
choice others should follow, to propose a decision.

### Decisions over time

Each decision may carry a `review` date. A routine (owned later by an inhabitant, for example
the secretary) puts due reviews in the needs-you inbox and in the morning message. A decision
that is replaced keeps its place with `status: superseded` and a link to the new one, so "why is
it like this?" always has an answer.

### Shiori, the chat

- **Sessions are saved** in `Sessions/` as notes, so a conversation can be found, linked and
  resumed. It runs as a resumable Claude Code session with the brain's and Openspace's tools,
  on the model chosen for Shiori (the latest Opus by default).
- **Three modes**, always visible and switched deliberately:
  - *Brainstorm*: it asks and suggests, creates nothing;
  - *Decide*: it may propose decisions and rules;
  - *Organise*: it turns the session into plans and tasks across offices, for approval.
- **The whiteboard** beside the chat fills in as the session goes: ideas, doubts, decisions taken.
  At the end it becomes a note, animated in the interface.
- **Answers cite** the notes they come from.
- **"Tomorrow morning"**: on a schedule, or at the end of a session, an agenda for the next day
  written into the vault and sent with the morning message.
- **Shiori as supervisor** replaces today's one-shot planner: it plans with the whole brain in
  view, assigns desks in every office and checks on work while it goes. In the building it is a
  character with its own look that walks between places, talks with inhabitants and visits the
  brain, never shut in an office of its own.

### Models per function

Settings › Models lists every function with its engine and model:

| Function | Default |
|---|---|
| Shiori (chat, supervisor) | Claude Code, latest Opus |
| Reviewer | as today |
| Digests and wiki summaries | Claude Code Sonnet, or a local model |
| Journals, chronicle, simulation lines | local model |
| Decider (yes/no, scores) | local small model |

### The wiki and the map

- **Wiki**: browse by folder and kind, full-text search, backlinks, history with diffs from the
  vault's git, decision status and review date, "why is this so?" from a file or a decision.
- **Map**: the graph of notes and links, filterable by office, kind and author, with visual
  themes (a galaxy, a city…) chosen like the building's.

### Voice

Push-to-talk first, then a wake word ("Shiori"). Only Shiori speaks.

- **Listening**: Whisper, locally (a whisper.cpp or faster-whisper server), already used on this
  kind of machine.
- **Speaking**: a local synthesiser, chosen by listening tests in Italian, English and Japanese.
  Candidates: **Kokoro** (82M parameters, Apache-2.0; Italian has two voices, graded C by its
  authors, against A for the best English ones, so it may sound flat in Italian), **Piper**
  (fast, several Italian voices), and larger models with better Italian whose licences must be
  checked before use.
- **Shiori's face** is abstract: eyes or a glow that show attention, thinking and answering, or
  a sphere of light with things in orbit, or a neural web. Chosen in the interface study. The
  dome with a brain is the brain's *place* in a science theme, not the face the person talks to.
- The open-source "J.A.R.V.I.S" voice assistant (React, Three.js, Claude Agent SDK, MIT) is a
  reference for the wake word, voice activity detection and a holographic look; ideas are taken,
  not the code as a whole.

### Inhabitants

An inhabitant is an identity (role, traits, look, theme) with tools, a vault area, a schedule
and a trust level. Today's desks are inhabitants whose job is code; routines become jobs of
inhabitants (the morning paper, mail sorting).

- **Trust levels** per action: does it alone, does it and tells, asks first. The routines'
  "automatic / ask first" switch, extended to everything.
- **Shifts**: who works by day and who by night, visible in the building.
- **Shared agenda**: the calendar every inhabitant reads (Google Calendar is already connected).
- **Agreements**: when two inhabitants' plans clash (a meeting at 14:00, a workout until 14:15),
  they meet (at one's place, the other's or halfway), propose under the constitution, and write
  the minutes. Limits on turns and messages keep them from talking forever; what the rules do not
  settle goes to the person as one question with their proposals.
- **Never against the person**: disagreements are between inhabitants, never with the person.

### Personalities and the social simulation

- **Traits** are fixed at creation; **mood** moves through the day.
- **Relationships remember real events**: a broken build, help with a bug, an agreement that
  went badly. Numbers come from what happened in Openspace, not from dice.
- **Quality is untouchable**: personality changes how they talk and move, whom they have coffee
  with, never what they deliver.
- **Agents do not see their numbers**; the person sees them on a character monitor page, with the
  history of every change.
- **Events**: birthdays (random or chosen, never with a year), a party when an epic closes,
  friends from other offices who meet now and then.
- **Seniority**: long work in a room makes an inhabitant its expert; their notes weigh more in
  that room's briefings.
- **Chronicle**: the office gossip goes into the person's morning paper and into `Chronicle/`.
- Cheap by design: the numbers are computed by code; a local model writes the lines.

### Worlds

A theme is a whole world, not a skin: places, how inhabitants move, how they meet, where the
brain is and how they consult it. Every office, inhabitant and link adds to the map, so the world
grows with the person. The first selection is made in the interface study: today's building of
offices, a science lab whose brain sits in a dome with particles orbiting it, and one more.

## Development rules

Founding rules for everyone who changes a project through Openspace (agents and Claude alike),
especially before production. Proposals to decide together, then written into the constitution:

1. Read before acting: constitution, decisions in scope, *Now*.
2. Every behaviour change comes with a test that fails without it; the test count only grows.
3. Work in a task's own copy; never in the folder the person runs from.
4. No secrets, personal data or real names in code, tests or documentation.
5. A choice others must follow becomes a decision with its reasons.
6. Production needs: green checks, a reviewer's verdict, the person's yes, and a way back.
7. Interfaces are checked at several widths, with contrast to WCAG AA, before they are called done.
8. When something is unclear, ask; when a resource of the person blocks a check, say so at once.

## Phases

| Phase | Content | Done when |
|---|---|---|
| **0. Foundations** | This document agreed; constitution and development rules written; models per function | The person approves both |
| **1. The brain's engine** | Vault, index, MCP tools, briefing, *Now* and holding overlapping work, decision proposals and reviews, journals, digests in the vault | Two desks never run on the same files unwarned; every merged task leaves a journal line |
| **2. Interface study and redesign** | Research of libraries and projects (animation, detailed themed pixel art, components, shaders, voice); prototypes to choose from; redesign view by view | The person picks a direction and the main views are redone |
| **3. Shiori on the new interface** | Wiki, map, chat with modes and whiteboard, "tomorrow morning", Shiori as roaming supervisor, voice | Brainstorm, decide and organise work from one session, by text or voice |
| **4. Inhabitants** | Identities (role/traits/look/theme), trust levels, shifts, shared agenda, agreements, routines as their jobs | Two inhabitants settle a calendar clash under the constitution |
| **5. Social life and worlds** | Traits, mood, relationships from events, character monitor, events, chronicle in the paper, the first extra worlds | The paper carries the week's chronicle |

## Risks and open questions

- **Quota**: Shiori on Opus all the time spends the Claude plan fast. Sessions should resume
  rather than restart, and work that does not need Opus goes elsewhere.
- **Privacy**: once mail, health and food enter the vault, those notes must stay on local models
  unless the person says otherwise; a per-folder "local only" flag may be needed.
- **Vault rot**: proposals, scopes and review dates keep it clean; a monthly tidy-up job may help.
- **Edits from both sides**: Obsidian and Openspace writing the same note at once; the vault's
  git and short writes keep it safe, conflicts become proposals.
- **Chatty agents**: limits on turns and an escalation path are part of the agreement protocol.
- **Open**: the vault per installation or per person; how far Shiori may accept decisions alone;
  which third world to start with.
