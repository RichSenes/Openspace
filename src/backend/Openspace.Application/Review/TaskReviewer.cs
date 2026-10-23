using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Domain.Work;

namespace Openspace.Application.Review;

/// <summary>
/// Reads finished work before a person does: the changes in the task's working copy,
/// against what was asked and the room's procedure. It leaves a note with its verdict and
/// never changes anything; the person still decides.
/// </summary>
public sealed partial class TaskReviewer(
    IWorkTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    IAuditLog audit,
    TimeProvider clock,
    ILogger<TaskReviewer> logger)
{
    /// <summary>How the reviewer signs its notes.</summary>
    public const string Name = "Claude review";

    private const string Instructions = """
        You review the work of an AI agent before a person accepts it. You may read files and run
        git status, git diff and git log in the working copy; you change nothing and run nothing
        else, on purpose: the agent ran the checks and reported their output as evidence.
        Start with git status and git diff to see every change, new files included, then read
        what you need around them. Check that the change does what was asked, nothing more;
        that it is correct, tested where the project has tests, and free of secrets or personal
        data; and that the evidence of the required procedure fits the change.
        Judge from the code and the evidence: never ask for changes only because you could not
        run something yourself. A finding names a real problem in the work, with its file.
        Be concrete and brief. Answer "ok" when you would accept the work as it is.
        In manualChecks list what a person must still try by hand before merging, because no
        test covers it (a screen, a flow on a phone, an email), each with how to try it: where
        to go, what to do, what should happen. Leave out the checks already listed.
        Write in the language the task is written in.
        """;

    private static readonly string Schema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            verdict = new { type = "string", @enum = new[] { "ok", "changes" } },
            summary = new { type = "string", description = "Two or three sentences on the work as a whole." },
            findings = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { file = new { type = "string" }, problem = new { type = "string" } },
                    required = new[] { "file", "problem" },
                },
            },
            manualChecks = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { what = new { type = "string" }, how = new { type = "string" } },
                    required = new[] { "what", "how" },
                },
            },
        },
        required = new[] { "verdict", "summary", "findings" },
    });

    public async Task ReviewAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is not { Status: WorkTaskStatus.Review, Run: not null })
        {
            return;
        }

        string? answer;
        try
        {
            answer = await runtime.ReviewAsync(task.Run.WorkspacePath, Instructions, Brief(task), Schema, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReviewFailed(logger, ex, taskId.Value);
            await NoteAsync(taskId, NoteAuthor.System, $"The review by Claude could not run ({ex.Message}). The work is ready for you anyway.", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (answer is null)
        {
            return;
        }

        var verdict = Verdict.Read(answer);
        await NoteAsync(taskId, NoteAuthor.Reviewer(Name), verdict.ToNote(), cancellationToken, verdict.ManualChecks).ConfigureAwait(false);
    }

    /// <summary>What the reviewer reads first: the request, the procedure with its evidence, the agent's notes.</summary>
    internal static string Brief(WorkTask task)
    {
        var brief = new StringBuilder();
        brief.AppendLine(CultureInfo.InvariantCulture, $"Task: {task.Title}");
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            brief.AppendLine().AppendLine(task.Description);
        }

        if (task.Procedure.Count > 0)
        {
            brief.AppendLine().AppendLine("Required procedure, as the agent reported it:");
            foreach (var step in task.Procedure)
            {
                brief.AppendLine(CultureInfo.InvariantCulture, $"{step.Number}. {step.Text}: {(step.IsDone ? $"done, evidence: {step.Evidence}" : "not reported")}");
            }
        }

        var notes = task.Notes.Where(n => n.Author.Kind is not NoteAuthorKind.Reviewer).TakeLast(8).ToList();
        if (notes.Count > 0)
        {
            brief.AppendLine().AppendLine("Notes on the task, oldest first:");
            foreach (var note in notes)
            {
                brief.AppendLine(CultureInfo.InvariantCulture, $"- {note.Author.Kind}: {note.Text}");
            }
        }

        if (task.ManualChecks.Count > 0)
        {
            brief.AppendLine().AppendLine("Checks already listed for a person to try by hand:");
            foreach (var check in task.ManualChecks)
            {
                brief.AppendLine(CultureInfo.InvariantCulture, $"- {check.What}");
            }
        }

        if (task.Run?.Branch is { } branch)
        {
            brief.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"The work is in branch {branch}, not committed yet.");
        }

        return brief.ToString();
    }

    private async Task NoteAsync(
        WorkTaskId taskId, NoteAuthor author, string text, CancellationToken cancellationToken, IReadOnlyList<(string What, string How)>? checks = null)
    {
        // Read again: the person may have accepted or sent back the work meanwhile.
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task.Status != WorkTaskStatus.Review)
        {
            return;
        }

        var now = clock.GetUtcNow();
        task.AddNote(author, text, now);
        if (author.Kind == NoteAuthorKind.Reviewer)
        {
            audit.Add(AuditEntry.Record(task.Id, now, AuditKind.Lifecycle, $"Reviewed by {Name}"));
        }

        foreach (var (what, how) in (checks ?? []).Take(WorkTask.MaxManualChecks - task.ManualChecks.Count))
        {
            task.AddManualCheck(what, how);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The review of task {TaskId} failed")]
    private static partial void LogReviewFailed(ILogger logger, Exception exception, Guid taskId);
}

/// <summary>The reviewer's answer, as a note people read at a glance: the verdict comes first.</summary>
internal sealed record Verdict(
    bool Ok, string Summary, IReadOnlyList<(string File, string Problem)> Findings, IReadOnlyList<(string What, string How)> ManualChecks)
{
    public static Verdict Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var findings = root.TryGetProperty("findings", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray()
                .Select(x => (Text(x, "file"), Text(x, "problem")))
                .Where(x => x.Item2.Length > 0)
                .ToList()
            : [];
        var checks = root.TryGetProperty("manualChecks", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray()
                .Select(x => (Text(x, "what"), Text(x, "how")))
                .Where(x => x.Item1.Length > 0)
                .Select(x => (x.Item1.Length <= 300 ? x.Item1 : x.Item1[..300], x.Item2))
                .ToList()
            : [];
        return new Verdict(Text(root, "verdict") == "ok" && findings.Count == 0, Text(root, "summary"), findings, checks);
    }

    public string ToNote()
    {
        var note = new StringBuilder(Ok ? "OK: ready to accept." : "Changes suggested.");
        if (Summary.Length > 0)
        {
            note.Append(' ').Append(Summary);
        }

        foreach (var (file, problem) in Findings)
        {
            note.AppendLine().Append("- ").Append(file.Length > 0 ? $"{file}: " : string.Empty).Append(problem);
        }

        var text = note.ToString();
        return text.Length <= 4000 ? text : text[..4000];
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : string.Empty;
}
