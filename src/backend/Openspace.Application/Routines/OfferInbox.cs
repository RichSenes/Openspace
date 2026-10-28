using System.Globalization;
using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Common;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <summary>
/// An offer the person found where no routine may read (a site that turns readers away) and
/// pastes in: it waits in the job offers routine's inbox, a file each, and the routine is asked
/// to run and judge it like the others.
/// </summary>
public sealed class OfferInbox(IRoutineRepository routines, RoutineRequests requests, TimeProvider clock)
{
    /// <summary>An offer's text shorter than this says too little to judge.</summary>
    public const int LeastText = 80;

    public const int MostText = 40_000;

    private const string LinkLine = "Link: ";

    public async Task AddAsync(RoutineId id, string text, string? link, CancellationToken cancellationToken)
    {
        var routine = await routines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (routine.Kind != "job-offers")
        {
            throw new DomainException("Only a job offers routine judges offers.");
        }

        text = text?.Trim() ?? string.Empty;
        if (text.Length is < LeastText or > MostText)
        {
            throw new DomainException($"Paste the whole offer: between {LeastText} and {MostText} characters.");
        }

        Uri? page = null;
        if (!string.IsNullOrWhiteSpace(link) && (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out page) || page.Scheme is not ("http" or "https")))
        {
            throw new DomainException("The link must be a web address, https://…");
        }

        using var settings = JsonDocument.Parse(routine.Settings);
        var inbox = MorningPaper.Expand(JobSettings.Read(settings.RootElement).Inbox);
        Directory.CreateDirectory(inbox);
        var name = $"{clock.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{RoutineContext.Fingerprint(text)[..8]}.md";
        await File.WriteAllTextAsync(Path.Combine(inbox, name), (page is null ? string.Empty : $"{LinkLine}{page}\n\n") + text + "\n", cancellationToken).ConfigureAwait(false);
        requests.Ask(routine.Id);
    }

    /// <summary>The link written on top, if any, and the offer's text.</summary>
    public static (Uri? Link, string Text) Read(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var text = file.Trim();
        if (!text.StartsWith(LinkLine, StringComparison.Ordinal))
        {
            return (null, text);
        }

        var end = text.IndexOf('\n', StringComparison.Ordinal);
        var line = end < 0 ? text : text[..end];
        return (Uri.TryCreate(line[LinkLine.Length..].Trim(), UriKind.Absolute, out var link) ? link : null, end < 0 ? string.Empty : text[(end + 1)..].Trim());
    }
}
