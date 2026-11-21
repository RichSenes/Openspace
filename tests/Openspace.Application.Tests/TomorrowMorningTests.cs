using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.Attention;
using Openspace.Application.Brain;
using Openspace.Application.Functions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>Tomorrow's agenda from a made-up calendar, office and vault: no real account, no real model.</summary>
public sealed class TomorrowMorningTests
{
    private readonly Harness _h = new();
    private readonly FakeAgenda _agenda = new();
    private readonly FakeChat _chat = new();
    private readonly FakeEngines _engines = new();
    private readonly FunctionModelOptions _models = new();
    private readonly LocalModelOptions _local = new() { Models = [new LocalModel { Id = "gemma", Name = "Gemma" }] };

    private TomorrowMorning Kind() => new(
        _agenda, new NeedsYouQueries(_h.Offices, _h.Tasks, _h.Routines, _h.Vault, _h.Clock), _h.Tasks, _h.Offices, _h.Vault, _chat, _engines,
        new LiveOptions<FunctionModelOptions>(() => _models), new LiveOptions<LocalModelOptions>(() => _local));

    private Routine Create(string account = "sam@example.com", string? language = null, int sessionDays = 7)
    {
        var settings = $$"""
            {"account":"{{account}}","channel":"STEWARD"{{(language is null ? "" : $",\"language\":\"{language}\"")}},"sessionDays":{{sessionDays}}}
            """;
        var routine = Routine.Create(_h.Office, "Tomorrow", "tomorrow", RoutineSchedule.Daily(new TimeOnly(21, 0)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    private async Task<RoutineRun> RunAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await new RoutineRunner(_h.Routines, _h.Offices, _h.Progress, [Kind()], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance)
            .RunWaitingAsync(default);
        return _h.Routines.Runs.Where(r => r.RoutineId == routine.Id).OrderBy(r => r.StartedAt).Last();
    }

    /// <summary>Wednesday 24 June 2015 at 21:00: the agenda is for Thursday 25 June.</summary>
    private void Evening() => _h.Clock.Advance(new DateTimeOffset(2015, 6, 24, 21, 0, 0, TimeSpan.Zero) - _h.Clock.GetUtcNow());

    private static readonly DateOnly Thursday = new(2015, 6, 25);

    private void Session(string path, string text, int daysAgo = 1) =>
        _h.Vault.Notes[path] = VaultFormat.Parse(path, text) with { Modified = _h.Clock.GetUtcNow().AddDays(-daysAgo) };

    /// <summary>Something from every source: calendar, tasks, the inbox, the queue and a session.</summary>
    private void FullDay()
    {
        _agenda.Entries.AddRange([
            new AgendaEntry(false, "Renew the library card", new DateOnly(2015, 6, 22), null, ""),
            new AgendaEntry(true, "Dentist", Thursday, new TimeOnly(9, 30), "Clinic"),
            new AgendaEntry(true, "Trip to the lake", Thursday, null, ""),
            new AgendaEntry(false, "Pay the electricity bill", Thursday, null, ""),
            new AgendaEntry(true, "Cinema", Thursday.AddDays(1), new TimeOnly(20, 0), ""),
        ]);
        _h.Tasks.Add(WorkTask.Propose(_h.Office, "Write the privacy page", null, "supervisor", _h.Clock.GetUtcNow().AddHours(-2)));
        _h.Enqueue("Add a logout button");
        _h.Enqueue("Fix the footer");
        Session("Sessions/2015-06-23-launch.md", """
            ---
            kind: session
            title: Planning the launch
            ---
            # Planning the launch

            ## Ideas

            - A landing page

            ## Open points

            - [ ] Choose the launch date
            - [x] Pick a name
            - Ask about the budget
            """);
    }

    [Fact]
    public async Task In_the_evening_it_writes_tomorrows_agenda_from_every_source_in_italian_and_sends_a_short_message()
    {
        var routine = Create();
        Evening();
        FullDay();
        _engines.Answer = "Domani si comincia dal dentista.";

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Equal("Agenda of 2015-06-25: 2 events, 2 tasks, 1 waiting for you, 2 queued, 2 open points.", run.Summary);
        Assert.Equal((Thursday, Thursday.AddDays(1)), _agenda.Asked.Single());

        var note = _h.Vault.Notes["Agenda/2015-06-25.md"];
        Assert.Equal("agenda", note.Kind);
        Assert.Equal("Shiori", note.Author);
        Assert.Equal("routine Tomorrow", note.From);
        Assert.Equal(
            """
            # Domani · giovedì 25 giugno

            Domani si comincia dal dentista.

            ## In calendario

            - Tutto il giorno: Trip to the lake
            - 09:30 Dentist (Clinic)

            ## Da fare

            - Renew the library card — scadeva lun 22 giu
            - Pay the electricity bill

            ## Ti aspettano in Openspace

            - Da approvare: Write the privacy page · E Corp

            ## In coda negli uffici

            - E Corp (2): Add a logout button, Fix the footer

            ## Rimasti aperti nelle sessioni

            - Choose the launch date · [[Sessions/2015-06-23-launch]]
            - Ask about the budget · [[Sessions/2015-06-23-launch]]
            """.ReplaceLineEndings("\n"),
            note.Body.TrimEnd('\n'));

        var (_, message) = Assert.Single(_h.Outbox.Posted);
        Assert.Equal(
            """
            Domani · giovedì 25 giugno

            Domani si comincia dal dentista.

            • Tutto il giorno: Trip to the lake
            • 09:30 Dentist (Clinic)

            Da fare (2)
            • Renew the library card — scadeva lun 22 giu
            • Pay the electricity bill

            • Ti aspettano in Openspace: 1
            • In coda negli uffici: E Corp 2
            • Rimasti aperti nelle sessioni: 2

            L'agenda completa è nel cervello: Agenda/2015-06-25.md
            """.ReplaceLineEndings("\n"),
            message.Plain);
        Assert.StartsWith("<b>Domani · giovedì 25 giugno</b>", message.Html, StringComparison.Ordinal);
        Assert.Contains("<b>Da fare (2)</b>", message.Html, StringComparison.Ordinal);
        Assert.EndsWith("<i>L'agenda completa è nel cervello: Agenda/2015-06-25.md</i>", message.Html, StringComparison.Ordinal);

        // The digests model wrote the opening, in the routine's language, from the gathered facts only.
        var asked = Assert.Single(_engines.Asked);
        Assert.Equal((ModelEngine.ClaudeCode, "sonnet"), (asked.Engine, asked.Model));
        Assert.Contains("Write in Italian", asked.Instructions, StringComparison.Ordinal);
        Assert.Contains("Event: 09:30 Dentist (Clinic)", asked.Text, StringComparison.Ordinal);
        Assert.Contains("Task, late (was due 22 June): Renew the library card", asked.Text, StringComparison.Ordinal);
        Assert.Contains("Waiting in Openspace (To approve): Write the privacy page", asked.Text, StringComparison.Ordinal);
        Assert.Contains("Queued in the office E Corp: 2 tasks", asked.Text, StringComparison.Ordinal);
        Assert.Contains("Left open in a session: Choose the launch date", asked.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Cinema", asked.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Pick a name", asked.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_anywhere_and_no_account_it_still_writes_a_free_day_without_asking_a_model()
    {
        var routine = Create(account: "");
        Evening();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Equal("Agenda of 2015-06-25: no calendar, 0 waiting for you, 0 queued, 0 open points.", run.Summary);
        Assert.Contains("No Google account in the settings", run.Log, StringComparison.Ordinal);
        Assert.Empty(_agenda.Asked);
        Assert.Empty(_engines.Asked);
        Assert.Equal(
            "# Domani · giovedì 25 giugno\n\nGiornata libera: niente in calendario e niente che ti aspetti.\n",
            _h.Vault.Notes["Agenda/2015-06-25.md"].Body);
        var (_, message) = Assert.Single(_h.Outbox.Posted);
        Assert.Equal(
            "Domani · giovedì 25 giugno\n\nGiornata libera: niente in calendario e niente che ti aspetti.\n\nL'agenda completa è nel cervello: Agenda/2015-06-25.md",
            message.Plain);
    }

    [Fact]
    public async Task An_empty_calendar_says_so_and_a_vault_without_sessions_is_no_problem()
    {
        var routine = Create();
        Evening();
        _h.Vault.Put("Sessions/README.md", "# Sessions\n\n## Next\n\n- Not a session\n");

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        var body = _h.Vault.Notes["Agenda/2015-06-25.md"].Body;
        Assert.Contains("## In calendario\n\nNiente in calendario.", body, StringComparison.Ordinal);
        Assert.Contains("## Da fare\n\nNessuna attività in scadenza.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Not a session", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("opencode/deepseek-v4.1-flash-free")]
    [InlineData("openrouter/some-model:free")]
    [InlineData("openrouter/marked-by-the-person")]
    [InlineData(null)]
    public async Task On_a_free_online_model_the_personal_agenda_is_written_from_the_template_and_the_log_says_why(string? model)
    {
        _models.FreeModels = ["OpenRouter/Marked-By-The-Person"];
        _models.Digests = new FunctionModel { Engine = ModelEngine.OpenCode, Model = model };
        var routine = Create();
        Evening();
        FullDay();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Empty(_engines.Asked);
        Assert.Empty(_chat.Asked);
        Assert.Contains("may be a free online model", run.Log, StringComparison.Ordinal);
        Assert.Contains("without a model", run.Log, StringComparison.Ordinal);
        Assert.Contains(
            "In programma: 2 impegni, 2 cose da fare, 1 cosa ti aspetta in Openspace e 2 punti aperti dalle sessioni.",
            _h.Vault.Notes["Agenda/2015-06-25.md"].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_opencode_model_on_the_persons_own_provider_writes_the_opening()
    {
        _models.Digests = new FunctionModel { Engine = ModelEngine.OpenCode, Model = "anthropic/claude-sonnet" };
        var routine = Create();
        Evening();
        FullDay();

        await RunAsync(routine);

        Assert.Equal((ModelEngine.OpenCode, "anthropic/claude-sonnet"), (_engines.Asked.Single().Engine, _engines.Asked.Single().Model));
    }

    [Fact]
    public async Task When_no_model_answers_the_plain_template_opens_the_agenda()
    {
        _engines.Fail = true;
        var routine = Create();
        Evening();
        FullDay();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Contains("No model answered (Claude Code is not here)", run.Log, StringComparison.Ordinal);
        Assert.Contains("In programma: 2 impegni", _h.Outbox.Posted.Single().Message.Plain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_local_digests_model_is_asked_through_the_graphics_card_queue()
    {
        _models.Digests = new FunctionModel { Engine = ModelEngine.Local };
        var routine = Create();
        Evening();
        FullDay();

        await RunAsync(routine);

        Assert.Empty(_engines.Asked);
        Assert.Equal("local/gemma", _chat.Models.Single());
        Assert.Contains("Una giornata piena.", _h.Vault.Notes["Agenda/2015-06-25.md"].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_english_the_agenda_and_the_model_use_english()
    {
        _engines.Fail = true;
        var routine = Create(language: "English");
        Evening();
        FullDay();

        await RunAsync(routine);

        var body = _h.Vault.Notes["Agenda/2015-06-25.md"].Body;
        Assert.StartsWith("# Tomorrow · Thursday 25 June", body, StringComparison.Ordinal);
        Assert.Contains("## In the calendar", body, StringComparison.Ordinal);
        Assert.Contains("- Renew the library card — was due Mon 22 Jun", body, StringComparison.Ordinal);
        Assert.Contains("On the list: 2 events, 2 tasks to do, 1 thing waiting for you in Openspace and 2 points left open in the sessions.", body, StringComparison.Ordinal);
        Assert.Contains("Write in English", _engines.Asked.Single().Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_in_the_morning_it_prepares_the_day_it_is_in()
    {
        var routine = Create();
        _h.Clock.Advance(new DateTimeOffset(2015, 6, 25, 7, 0, 0, TimeSpan.Zero) - _h.Clock.GetUtcNow());

        await RunAsync(routine);

        Assert.Equal((Thursday, Thursday.AddDays(1)), _agenda.Asked.Single());
        Assert.StartsWith("# Oggi · giovedì 25 giugno", _h.Vault.Notes["Agenda/2015-06-25.md"].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_calendar_that_cannot_be_read_leaves_the_rest_of_the_agenda()
    {
        _agenda.Fail = true;
        var routine = Create();
        Evening();
        FullDay();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Contains("The calendar could not be read (Calendar is down.)", run.Log, StringComparison.Ordinal);
        var body = _h.Vault.Notes["Agenda/2015-06-25.md"].Body;
        Assert.DoesNotContain("## In calendario", body, StringComparison.Ordinal);
        Assert.Contains("## In coda negli uffici", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sessions_older_than_the_setting_say_nothing()
    {
        var routine = Create(sessionDays: 3);
        Evening();
        Session("Sessions/old.md", "# Old\n\n## Next steps\n\n- Something from long ago\n", daysAgo: 10);
        Session("Sessions/new.md", "# New\n\n## Next steps\n\n- Something from yesterday\n", daysAgo: 1);

        await RunAsync(routine);

        var body = _h.Vault.Notes["Agenda/2015-06-25.md"].Body;
        Assert.Contains("- Something from yesterday · [[Sessions/new]]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("long ago", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_long_day_shows_a_few_lines_in_the_message_and_all_of_them_in_the_note()
    {
        var routine = Create();
        Evening();
        for (var hour = 8; hour < 14; hour++)
        {
            _agenda.Entries.Add(new AgendaEntry(true, $"Meeting {hour}", Thursday, new TimeOnly(hour, 0), ""));
        }

        await RunAsync(routine);

        var plain = _h.Outbox.Posted.Single().Message.Plain;
        Assert.Contains("• 10:00 Meeting 10\n• e altre 3", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Meeting 11", plain, StringComparison.Ordinal);
        Assert.Contains("- 13:00 Meeting 13", _h.Vault.Notes["Agenda/2015-06-25.md"].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_points_are_the_unticked_items_under_open_or_next_headings_in_either_language()
    {
        var items = TomorrowMorning.OpenItems("""
            # A session

            ## Decisions

            - Not open

            ## Punti aperti

            - [ ] Primo
            - [x] Fatto
            ### Dettagli
            * Secondo
            1. Terzo

            ## Notes

            - Not open either

            ## Prossimi passi

            + Quarto
            """);

        Assert.Equal(["Primo", "Secondo", "Terzo", "Quarto"], items);
        Assert.Empty(TomorrowMorning.OpenItems("# Nothing\n\nJust words."));
    }

    [Theory]
    [InlineData(ModelEngine.ClaudeCode, null, true)]
    [InlineData(ModelEngine.Local, null, true)]
    [InlineData(ModelEngine.OpenCode, "local/gemma", true)]
    [InlineData(ModelEngine.OpenCode, "opencode/big-model-free", false)]
    [InlineData(ModelEngine.OpenCode, "  ", false)]
    public void Only_models_that_are_not_free_online_ones_may_read_personal_data(ModelEngine engine, string? model, bool may) =>
        Assert.Equal(may, PersonalDataReaders.MayRead(new FunctionModel { Engine = engine, Model = model }));

    [Theory]
    [InlineData("zen/grok-code", false)]
    [InlineData(" ZEN/GROK-CODE ", false)]
    [InlineData("anthropic/claude-sonnet", true)]
    public void Models_the_person_marked_as_free_may_not_read_personal_data_either(string model, bool may) =>
        Assert.Equal(may, PersonalDataReaders.MayRead(new FunctionModel { Engine = ModelEngine.OpenCode, Model = model }, ["zen/grok-code", " "]));

    private sealed class FakeAgenda : IAgenda
    {
        public List<AgendaEntry> Entries { get; } = [];

        public List<(DateOnly From, DateOnly Until)> Asked { get; } = [];

        public bool Fail { get; set; }

        public Task<string> AddAsync(string account, AgendaItem item, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The agenda never writes.");

        public Task<IReadOnlyList<AgendaEntry>> ReadAsync(string account, DateOnly from, DateOnly until, TimeZoneInfo zone, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Calendar is down.");
            }

            Asked.Add((from, until));
            return Task.FromResult<IReadOnlyList<AgendaEntry>>([.. Entries.Where(e => e.Date < until && (e.Date >= from || !e.IsEvent)).OrderBy(e => e.Date).ThenBy(e => e.IsEvent)]);
        }
    }

    private sealed class FakeChat : ILocalChat
    {
        public List<string> Asked { get; } = [];

        public List<string> Models { get; } = [];

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            Asked.Add(ask.User);
            Models.Add(ask.Model);
            return Task.FromResult("""{"intro":"Una giornata piena."}""");
        }
    }

    private sealed class FakeEngines : IEngineText
    {
        public List<(ModelEngine Engine, string? Model, string Instructions, string Text)> Asked { get; } = [];

        public string Answer { get; set; } = "A full day.";

        public bool Fail { get; set; }

        public Task<string> AskAsync(ModelEngine engine, string? model, string instructions, string text, CancellationToken cancellationToken)
        {
            Asked.Add((engine, model, instructions, text));
            return Fail ? throw new InvalidOperationException("Claude Code is not here") : Task.FromResult(Answer);
        }
    }
}
