# Shiori: user stories

The work of [brain.md](brain.md) split into stories that one agent can build, test and hand
back. Phases 0 and 1 are detailed; later phases are outlined and get detailed when their turn
comes (the interface study of phase 2 decides how phases 3–5 look).

## How the work runs

- **Lanes** keep parallel agents out of each other's files. Two stories in different lanes may
  run at the same time; stories in the same lane run one after the other.
- Each story lists the files it **owns**. An agent changes only those, plus tests and docs; if it
  needs another file, it stops and says so.
- Every story ends with: tests that fail without it, `dotnet test`, `tsc`, `oxlint`, `vitest`,
  screenshots at 1400 and 390 px for anything visible, and its README lines.
- Agents work in their own git worktree on a branch per story. Claude reviews each branch, merges
  main into it, and merges it into main.
- The person approves the end of each phase.

| Lane | Area |
|---|---|
| **V** | Vault: files, index, watcher (`Openspace.Application/Brain`, `Openspace.Infrastructure/Brain`) |
| **A** | Agents: MCP tools, briefing (`Openspace.Api/Mcp`, `Openspace.Application/Agents/Briefing.cs`) |
| **Q** | Queue and *Now* (`Openspace.Application/Queue`, merge checks) |
| **R** | Routines and inbox (`Openspace.Application/Routines/Kinds`, `Openspace.Application/Attention`) |
| **S** | Settings (`Openspace.Api/Settings`, `src/frontend/src/settings`) |
| **U** | Interface (`src/frontend/src`, outside settings) |

## Phase 0: foundations

**S0.1 Constitution and development rules** · lane: none (a conversation)
As the person, I want my rules and the development rules written down, so that every agent works
by them.
- Done: the rules were decided with the person and are in brain.md, *Development rules*.
- The template ships with the vault (S1.1); the person's own copy, in their language, goes into
  their vault when it exists.

**S0.2 Models per function** · lane S · owns `Openspace.Api/Settings/ModelsEndpoints.cs`, `settings/ModelsSection.tsx`
As the person, I want to choose the engine and model of each function, so that Opus goes where it
matters and local models do the rest.
- Settings › Models lists: Shiori, reviewer, digests, journals and chronicle, decider.
- Shiori defaults to Claude Code with the `opus` alias (the latest Opus).
- Saved in the settings file, applied without restart; unknown models are flagged as today.
- Today's "Supervisor and reviewer" choices move here, with their current values kept.

## Phase 1: the brain's engine

**S1.1 The vault on disk** · lane V · owns `Application/Brain/Vault*.cs`, `Infrastructure/Brain/FileVault.cs`
As an installation, I want a vault created with the standard structure, so that everyone starts
from the same organised brain.
- On start, if missing, the folder (default `~/.local/share/openspace/brain`, setting `Brain:Folder`) is
  created with its folders and a README in each, the constitution template with the development
  rules of brain.md, and `git init` with a first commit.
- Existing files are never overwritten.
- Notes are read with their front matter into a `Note` (kind, status, scope, review, links).
- Writes are atomic and committed with the author in the message.
- Tests with a temporary folder: structure, front matter round trip, no overwrite, a commit per write.

**S1.2 Index and search** · lane V · owns `Application/Brain/BrainSearch.cs`, `Infrastructure/Brain/BrainIndex.cs`, migration
As an agent or a person, I want to find notes by words and scope, so that answers are quick and
relevant.
- SQLite FTS5 table rebuilt on start; a file watcher updates it within seconds of an edit made
  outside Openspace (Obsidian).
- Ranking: text, then scope match (office, room, paths), status (accepted > proposed > superseded),
  recency.
- `GET /api/brain/search?q=&office=&room=&path=`, `GET /api/brain/notes/{path}`.
- Tests: ranking order, superseded last, an external edit found after the watcher fires.

**S1.3 Brain tools for agents** · lane A · owns the brain tools in `Api/Mcp`, depends on S1.1, S1.2
As an agent, I want to search, read, write my journal and propose decisions, so that I work with
what was decided and leave a trace.
- Tools `brain_search`, `brain_read`, `brain_journal`, `brain_propose`, `brain_now`.
- `brain_journal` writes only in `Inhabitants/<desk>/journal.md`; `brain_propose` only creates
  `status: proposed` notes.
- Tests through the MCP endpoint, as the existing tools are tested.

**S1.4 Briefing reads the brain** · lane A · owns `Briefing.cs`, depends on S1.2
As an agent starting a task, I want the rules, the decisions in scope and the work nearby in my
briefing, so that I do not undo or collide with others.
- A "Before you start" part: constitution rules, accepted decisions whose scope matches the task's
  office, room or paths (at most N, most relevant first), overlaps from *Now*, the room's expert
  notes.
- At the end of the task the agent is asked for a journal line and, when it made a lasting choice,
  a proposal.
- Tests on the briefing text with a fake vault.

**S1.5 Now: who changes what** · lane Q · owns `Application/Queue/WorkInProgress.cs`, `Now.md` writer
As the office, I want to know which files every running task is changing, so that overlapping work
is seen before it conflicts.
- Every few seconds, the changed files of each running task's working copy (and the paths its
  plan named) are collected and written to `Now.md` (Openspace is its only author).
- `GET /api/brain/now`.
- Tests with fake workspaces.

**S1.6 Hold overlapping work** · lane Q · owns the dispatcher's overlap rule, depends on S1.5
As the person, I want a queued task to wait when its files are being changed by another, so that
merges stop conflicting (constitution rule 6, on by default).
- A queued task whose planned paths or room overlap a running task's changed files waits, with
  the reason shown as today's waits ("waits for Screens, who is changing src/web/cart.ts").
- An office setting turns it off.
- Before a merge, branches that touched the same files as another unmerged branch are flagged.
- Tests on the dispatcher.

**S1.7 Shiori accepts, the person reviews** · lane R · owns `Attention` additions, depends on S1.1
As the person, I want proposals accepted by Shiori and shown to me afterwards, so that work goes on
and I keep the last word.
- A proposal is accepted (or sent back with a reason) by Shiori; until Shiori exists as a chat, a
  rule accepts proposals whose scope is one office and that cite no conflicting accepted decision,
  and leaves the rest proposed for the person.
- New needs-you kind *Decision to review* with Fine / Open / Undo; undoing sets `status: rejected`,
  commits, and adds a note to the proposing agent's journal.
- Tests on the query and the commands.

**S1.8 Reviews come back** · lane R · owns a routine kind `decision-reviews`, depends on S1.7
As the person, I want decisions due for review to come back to me, so that the brain does not age
silently.
- A routine lists decisions whose `review` date is due, in the inbox and in its message.
- Tests with a fake clock.

**S1.9 Digests in the vault** · lane R · owns the weekly routine's vault output
As the person, I want the weekly (and monthly) summary kept in the vault, so that the wiki has the
history while the routine still sends me the report.
- The weekly routine also writes `Digests/<year>-W<week>.md`; a monthly one is added.
- Tests on the written note.

**S1.10 Settings › Brain** · lane S · owns `settings/BrainSection.tsx`, depends on S1.1
As the person, I want to see where the vault is, how many notes it holds and open it in Obsidian,
so that the brain is not a hidden folder.
- Folder (changeable), counts by kind, last change, "Open in Obsidian" link (`obsidian://open?path=`).

**Phase 1 is done** when, in a test office with two desks, overlapping tasks are held or flagged,
every accepted task leaves a journal line, a proposal goes from an agent to the inbox and back, and
the vault opens in Obsidian with its history.

## Phase 2: interface study and redesign (outline)

- **S2.1 Study**: every project the person sent (including the J.A.R.V.I.S assistant, pixelfx,
  interior.dev, Habblaud as a competitor, the 3D, liquid glass, shader and GSAP repositories),
  plus a GitHub search for animation libraries, detailed and themed pixel art assets with free
  licences, interface kits, shader effects and local voice (listening tests of Kokoro, Piper and
  others in Italian, English and Japanese). For each: what it gives, licence, weight, fit. Written
  into `Studies/`.
- **S2.2 Direction**: two or three prototypes of the same screens (the building with the brain's
  place, Shiori's chat and face) for the person to choose.
- **S2.3 onwards**: the redesign, view by view, one story per view, lanes by folder.

## Phase 3: Shiori on the new interface (outline)

Wiki (browse, search, backlinks, history, "why is this so?"), map with themes, chat with saved
sessions and three modes, the animated whiteboard turning into a note, "tomorrow morning",
Shiori as the roaming supervisor across offices, voice (push-to-talk, then the wake word).

## Phase 4: inhabitants (outline)

Identity as role + traits + look + theme, chosen separately; trust levels per action; shifts;
shared agenda; the agreement protocol with meetings, minutes and escalation; routines as
inhabitants' jobs.

## Phase 5: social life and worlds (outline)

Traits and mood, relationships from real events, the character monitor, events and birthdays
without a year, seniority as expertise, the chronicle in the morning paper, the first extra worlds.
