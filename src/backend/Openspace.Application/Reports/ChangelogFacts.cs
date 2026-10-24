using System.Globalization;
using System.Text;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Reports;

/// <summary>
/// What was accepted, as the changelog writer reads it: each request with what the agent
/// and the reviewer said they did. It is also the changelog when no model answers.
/// </summary>
public static class ChangelogFacts
{
    private const int MaxTextLength = 800;

    public static string Build(Office office, IReadOnlyList<WorkTask> accepted, DateTimeOffset? since)
    {
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(accepted);
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# {office.Name}: work accepted {(since is { } s ? $"since {s:d MMM yyyy}" : "so far")}");

        foreach (var task in accepted)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {task.Title}");
            if (task.Description.Length > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- Asked: {Short(task.Description)}");
            }

            if (task.Notes.LastOrDefault(n => n.Author.Kind == NoteAuthorKind.Desk) is { } summary)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- The agent's summary: {Short(summary.Text)}");
            }

            if (task.Notes.LastOrDefault(n => n.Author.Kind == NoteAuthorKind.Reviewer) is { } review)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- The reviewer: {Short(review.Text)}");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"- Accepted on {task.StatusChangedAt:d MMM yyyy}{(task.MergedInto is { } target ? $", merged into {target}" : string.Empty)}");
        }

        return text.ToString();
    }

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxTextLength ? line : string.Concat(line.AsSpan(0, MaxTextLength), "…");
    }
}
