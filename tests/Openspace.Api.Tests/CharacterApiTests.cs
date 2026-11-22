using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Openspace.Api.Tests;

public sealed class CharacterApiTests : IDisposable
{
    private static readonly string[] ShyAndMeticulous = ["Shy", "Meticulous"];
    private static readonly string[] ShyAndChatty = ["Shy", "Chatty"];

    private readonly OpenspaceApp _app = new();
    private readonly HttpClient _http;

    public CharacterApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private static async Task<Guid> IdOf(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_desk_is_born_with_chosen_traits_and_the_person_sets_its_birthday_shift_and_trust()
    {
        var office = await IdOf(await _http.PostAsJsonAsync("/api/offices", new { name = "E Corp" }));
        var room = await IdOf(await _http.PostAsJsonAsync($"/api/offices/{office}/rooms", new { name = "Web" }));
        var desk = await IdOf(await _http.PostAsJsonAsync($"/api/offices/{office}/rooms/{room}/desks",
            new { name = "Frontend", agentName = "build", traits = ShyAndMeticulous }));
        var opposites = await _http.PostAsJsonAsync($"/api/offices/{office}/rooms/{room}/desks",
            new { name = "Backend", agentName = "build", traits = ShyAndChatty });

        var url = $"/api/offices/{office}/desks/{desk}";
        Assert.Equal(HttpStatusCode.NoContent, (await _http.PutAsJsonAsync($"{url}/birthday", new { month = 2, day = 29 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _http.PutAsJsonAsync($"{url}/birthday", new { month = 4, day = 31 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _http.PutAsJsonAsync($"{url}/shift", new { shift = "Night" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _http.PutAsJsonAsync($"{url}/trust", new { action = "SafeRequests", level = "AsksFirst" })).StatusCode);

        var board = await _http.GetFromJsonAsync<JsonElement>($"/api/offices/{office}/characters");
        var character = board.GetProperty("characters").EnumerateArray().Single();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, opposites.StatusCode);
        Assert.Equal(["Shy", "Meticulous"], character.GetProperty("traits").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal((2, 29, true), (character.GetProperty("birthday").GetProperty("month").GetInt32(), character.GetProperty("birthday").GetProperty("day").GetInt32(),
            character.GetProperty("birthday").GetProperty("chosen").GetBoolean()));
        Assert.Equal("Night", character.GetProperty("shift").GetString());
        Assert.Equal("AsksFirst", character.GetProperty("trust").GetProperty("SafeRequests").GetString());
        Assert.Equal("Alone", character.GetProperty("trust").GetProperty("Journal").GetString());
        Assert.Equal(Application.Characters.Mood.BaselineOf([Domain.Offices.Trait.Shy, Domain.Offices.Trait.Meticulous]), character.GetProperty("mood").GetProperty("value").GetInt32());
        Assert.Empty(board.GetProperty("relationships").EnumerateArray());
    }

    [Fact]
    public async Task The_agenda_reads_the_next_days_and_leaves_the_calendar_out_without_an_account()
    {
        var office = await IdOf(await _http.PostAsJsonAsync("/api/offices", new { name = "E Corp" }));

        var agenda = await _http.GetFromJsonAsync<JsonElement>($"/api/offices/{office}/agenda?days=3");

        var (from, until) = (DateOnly.Parse(agenda.GetProperty("from").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(agenda.GetProperty("until").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(3, until.DayNumber - from.DayNumber);
        Assert.Equal(JsonValueKind.Null, agenda.GetProperty("calendar").ValueKind);
        Assert.Empty(agenda.GetProperty("lines").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/offices/{Guid.NewGuid()}/agenda")).StatusCode);
    }

    [Fact]
    public async Task Agreements_are_listed_per_office_and_an_unknown_one_cannot_be_decided()
    {
        var office = await IdOf(await _http.PostAsJsonAsync("/api/offices", new { name = "E Corp" }));

        Assert.Empty((await _http.GetFromJsonAsync<JsonElement>($"/api/offices/{office}/characters/agreements")).EnumerateArray());
        var unknown = await _http.PostAsJsonAsync($"/api/agreements/{Guid.NewGuid()}/decide", new { goesFirst = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
