using Openspace.Application.Brain;

namespace Openspace.Application.Tests;

public class JournalTests
{
    [Fact]
    public async Task Two_entries_written_at_once_both_stay_under_one_day()
    {
        var vault = new InMemoryVault();
        var now = new DateTimeOffset(2026, 11, 21, 9, 30, 0, TimeSpan.Zero);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Journals.AppendAsync(vault, "Screens", now, $"entry {i}", i % 2 == 0 ? "Screens" : "Shiori", "wrote", default)));

        var journal = await vault.ReadAsync(Journals.PathOf("Screens"), default);
        Assert.NotNull(journal);
        Assert.Equal(20, journal!.Body.Split('\n').Count(l => l.StartsWith("- 09:30 · entry ", StringComparison.Ordinal)));
        Assert.Equal(1, journal.Body.Split('\n').Count(l => l == "## 2026-11-21"));
    }

    [Fact]
    public void A_desk_name_becomes_one_safe_folder() =>
        Assert.Equal("Front-desk", Journals.FolderName(".Front/desk"));
}
