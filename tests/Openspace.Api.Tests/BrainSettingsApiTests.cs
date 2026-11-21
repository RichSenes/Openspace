using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Openspace.Api.Tests;

public sealed class BrainSettingsApiTests : IDisposable
{
    private readonly OpenspaceApp _app = new();
    private readonly HttpClient _http;

    public BrainSettingsApiTests()
    {
        Directory.CreateDirectory(Path.Combine(_app.BrainFolder, "Decisions"));
        File.WriteAllText(Path.Combine(_app.BrainFolder, "Decisions", "2026-11-20-merge-order.md"), "---\nkind: decision\nstatus: accepted\n---\n# Merge order\n\nOldest first.\n");
        _http = _app.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task The_summary_tells_where_the_vault_is_and_what_it_holds()
    {
        var summary = await _http.GetFromJsonAsync<JsonElement>("/api/brain/summary");

        Assert.Equal(Path.GetFullPath(_app.BrainFolder).TrimEnd('/'), summary.GetProperty("folder").GetString());
        Assert.Equal(1, summary.GetProperty("kinds").GetProperty("decision").GetInt32());
        Assert.True(summary.GetProperty("notes").GetInt32() >= 1);
        Assert.False(summary.GetProperty("kinds").TryGetProperty("note", out var readmes) && readmes.GetInt32() > 2, "the folders' READMEs are not counted");
    }

    [Fact]
    public async Task A_new_folder_is_kept_for_the_next_start_and_a_relative_one_is_refused()
    {
        var moved = await _http.PutAsJsonAsync("/api/brain/folder", new { folder = "~/Documents/Brain/" });
        var refused = await _http.PutAsJsonAsync("/api/brain/folder", new { folder = "brain" });

        // The test's own settings come after the file, so the file is read to see what was kept.
        var summary = await moved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("\"Folder\": \"~/Documents/Brain\"", await File.ReadAllTextAsync(_app.SettingsPath), StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(_app.BrainFolder).TrimEnd('/'), summary.GetProperty("folder").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }
}
