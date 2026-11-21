namespace Openspace.Infrastructure.Brain;

/// <summary>
/// The empty, organised vault every installation starts from: the same folders for everyone, each
/// with a note saying what goes there. It ships the shape only; what the person writes stays theirs.
/// </summary>
internal static class VaultTemplate
{
    public static IReadOnlyList<(string Path, string Text)> Files { get; } =
    [
        ("README.md", """
            # Shiori

            This is Openspace's shared brain: plain Markdown notes that agents read before they act
            and write when they are done. Obsidian opens this folder as a vault, but it is not needed.

            - `Constitution.md` holds the rules, numbered. They win over everything else.
            - `Now.md` is written by Openspace only: who is changing which files, right now.
            - Every other folder has a README saying what goes in it.

            Every note starts with front matter (`kind`, `status`, `offices`, `rooms`, `paths`,
            `review`, `supersedes`, `from`, `author`); fields that do not apply are left out. Links
            between notes are wikilinks, like `[[2026-11-20-merge-order]]`.

            The folder is a local git repository: every change Openspace makes is a commit with its
            author. Openspace never adds a remote to it.

            """),
        ("Constitution.md", """
            ---
            kind: rule
            status: accepted
            author: Openspace
            ---
            # Constitution

            The person's rules, numbered. Agents cite them as "rule 4". They win over every other note,
            and only the person changes them.

            The founding rules, for everyone who changes a project through Openspace:

            1. Read before acting: the constitution, the decisions in scope, *Now*.
            2. Every behaviour change comes with a test that fails without it; the number of tests never
               goes down. Interfaces are also checked at several widths and for WCAG AA contrast.
            3. Work only in the task's own copy. Outside it (the person's folders, the system, pushes,
               deployments, keys) the person says yes first.
            4. No secrets, personal data or real names in code, tests or repository documents.
            5. A choice others must follow becomes a decision with its reasons. Shiori accepts proposals; the
               person reviews them afterwards and can undo them.
            6. Two pieces of work on the same files do not run side by side: the second waits and says why.
            7. Production needs green checks, the reviewer's verdict, the person's explicit yes and a way back.
            8. When something is unclear, search the brain first, then ask Shiori, who answers or passes the
               question to the person. Never guess on what cannot be undone.
            9. Personal data (mail, health, food) may be read by local models and by Claude on the person's
               account, never by free online models.
            10. Notes are written in the person's language; code, commits and repository documents in English.
            11. No agent ever acts against the person: disagreements stay between agents.
            12. Personality changes how agents talk, never the quality of their work.
            13. One voice talks with the person: Shiori's.

            """),
        ("Now.md", """
            ---
            kind: note
            author: Openspace
            ---
            # Now

            Written by Openspace only: the tasks running right now and the files each is changing.
            Edits made here are replaced on the next update.

            Nobody is working right now.

            """),
        ("Decisions/README.md", """
            # Decisions

            Choices with their reasons, one note each, named by date and subject:
            `2026-11-20-merge-order.md`.

            A decision says what was chosen, why, and what was left out. Its front matter gives its
            scope (`offices`, `rooms`, `paths`), its `status` (proposed, accepted, superseded,
            rejected) and when to `review` it. Agents propose decisions; Shiori accepts them and the
            person reviews them afterwards. A decision that is replaced stays here with
            `status: superseded` and a link to the one that replaced it.

            """),
        ("Plans/README.md", """
            # Plans

            Longer plans, with their phases and what "done" means for each. A plan links the
            decisions and studies it builds on.

            """),
        ("Studies/README.md", """
            # Studies

            Research: libraries, projects and options compared, with what each gives, its licence
            and how well it fits. Decisions link the studies they come from.

            """),
        ("Sessions/README.md", """
            # Sessions

            Conversations with Shiori and their whiteboards: the ideas, doubts and decisions of each
            session, so a conversation can be found, linked and resumed.

            """),
        ("Digests/README.md", """
            # Digests

            Weekly and monthly summaries of what happened, written by Openspace's routines:
            `2026-W47.md`, `2026-11.md`.

            """),
        ("Agenda/README.md", """
            # Agenda

            The next day's agenda, one note per day, written by the "Tomorrow morning" routine:
            `2026-11-22.md`.

            """),
        ("Offices/README.md", """
            # Offices

            One folder per office, named like the office: an overview of its project, its
            conventions and its glossary. Notes here apply to that office's work.

            """),
        ("Inhabitants/README.md", """
            # Inhabitants

            One folder per inhabitant (every desk is one): `profile.md` with its role, traits and
            look, and `journal.md` with what it touched, learnt and left half done. Each inhabitant
            writes only its own journal.

            """),
        ("Chronicle/README.md", """
            # Chronicle

            Social events of the office (parties, meetings, friendships) for the morning paper and
            the wiki.

            """),
        (".gitignore", """
            # Obsidian's window layout and bin change all the time and are nobody's notes.
            .obsidian/workspace*.json
            .trash/

            """),
    ];
}
