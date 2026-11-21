using System.Globalization;

namespace Openspace.Application.Brain;

/// <summary>
/// The inhabitants' journals: one note each, a heading per day and a line per entry. Desks write
/// their own; Shiori writes there what became of their proposals. One append at a time, because
/// an append reads the note and writes it back, and two at once would lose a line.
/// </summary>
public static class Journals
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>A name as one folder of the vault: no slashes, no leading dot.</summary>
    public static string FolderName(string name)
    {
        var safe = new string([.. name.Select(c => c is '/' or '\\' or ':' || char.IsControl(c) ? '-' : c)]).Trim().TrimStart('.');
        return safe.Length > 0 ? safe : "Desk";
    }

    public static string PathOf(string desk) => $"Inhabitants/{FolderName(desk)}/journal.md";

    /// <summary>Appends "- HH:mm · text" under the day's heading of the desk's journal, creating it when missing.</summary>
    /// <param name="author">Who writes: the desk itself, or Shiori.</param>
    /// <returns>The journal's path.</returns>
    public static async Task<string> AppendAsync(
        IVault vault, string desk, DateTimeOffset now, string text, string author, string summary, CancellationToken cancellationToken)
    {
        var path = PathOf(desk);
        var day = $"## {now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var entry = $"- {now.ToString("HH:mm", CultureInfo.InvariantCulture)} · {text.ReplaceLineEndings(" ")}\n";

        await OneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var journal = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false)
                ?? new Note(path, "journal", null, $"{desk}'s journal", [], [], [], null, null, null, desk, $"# {desk}'s journal\n", []);
            var body = journal.Body.Length == 0 || journal.Body.EndsWith('\n') ? journal.Body : journal.Body + "\n";

            // Entries of the same day go under one heading, so the journal reads day by day.
            var lastDay = body.Split('\n').LastOrDefault(l => l.StartsWith("## ", StringComparison.Ordinal))?.TrimEnd();
            body += lastDay == day ? entry : $"\n{day}\n\n{entry}";
            await vault.WriteAsync(journal with { Body = body }, author, cancellationToken, summary).ConfigureAwait(false);
            return path;
        }
        finally
        {
            OneAtATime.Release();
        }
    }
}
