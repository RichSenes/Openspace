using System.Globalization;
using System.Text;
using Openspace.Application.Abstractions;
using Openspace.Application.Attention;
using Openspace.Application.Brain;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// Brings back the accepted decisions whose review date has come, so the brain does not age
/// silently: once a run has looked, they wait in the needs-you inbox (keep, open, supersede), and
/// the routine's channel, when it names one, gets the list. It only reads the vault; the person's
/// answers change the notes.
/// </summary>
public sealed class DecisionReviews(IVault vault, IOfficeRepository offices) : IRoutineKind
{
    public string Key => NeedsYouQueries.ReviewsKind;

    public string Name => "Decision reviews";

    public string Description =>
        "Every day looks for the office's decisions due for review and brings them back to you, in Openspace and in the chat.";

    public string DefaultSettings => """
        {
          "channel": ""
        }
        """;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var office = (await offices.FindAsync(context.Routine.OfficeId, cancellationToken).ConfigureAwait(false))?.Name;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, context.Clock.LocalTimeZone).DateTime);
        context.Progress("Reading the decisions in the brain");
        var due = DecisionRules.Due(await vault.ListAsync(cancellationToken).ConfigureAwait(false), office, today)
            .OrderBy(n => n.Review).ThenBy(n => n.Path, StringComparer.Ordinal).ToList();
        if (due.Count == 0)
        {
            return "No decision is due for review.";
        }

        var text = new StringBuilder($"Decisions to look at again ({due.Count})");
        foreach (var note in due)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n• {note.Title}, due {DecisionRules.Day(note.Review!.Value)}");
        }

        text.Append("\n\nKeep or supersede them in Openspace, under \"need you\".");
        context.Write($"{due.Count} due: {string.Join(", ", due.Select(n => n.Path))}");
        await context.DeliverAsync(text.ToString(), null, cancellationToken).ConfigureAwait(false);
        return due.Count == 1 ? "1 decision due for review." : $"{due.Count} decisions due for review.";
    }
}
