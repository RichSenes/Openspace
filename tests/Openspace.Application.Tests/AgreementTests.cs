using Openspace.Application.Attention;
using Openspace.Application.Characters;
using Openspace.Application.Queue;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>Agreements between desks whose planned work clashes, on a made-up office: no model unless a fake one.</summary>
public sealed class AgreementTests
{
    private readonly Harness _h = new();
    private readonly InMemoryAgreements _agreements = new();
    private readonly Engines _engines = new();
    private readonly Desk _screens;

    public AgreementTests() => _screens = _h.Office.AddDesk(_h.Room.Id, "Screens", AgentProfile.Create("designer"), Harness.Start);

    private Functions.FunctionModel _journals = new() { Engine = Functions.ModelEngine.ClaudeCode, Model = "haiku" };

    private AgreementKeeper Keeper() => new(
        _h.Offices, _h.Tasks, _agreements, _h.Vault, _h.UnitOfWork, new NoChat(), _engines,
        new LiveOptions<Functions.FunctionModelOptions>(() => new() { Journals = _journals }),
        new LiveOptions<LocalModels.LocalModelOptions>(() => new()), _h.Clock);

    private QueueDispatcher Dispatcher() =>
        new(_h.Tasks, _h.Offices, _h.Limits, _h.Gpu, _h.Waits, _h.Work, _h.UnitOfWork, new LiveOptions<QueueOptions>(() => new QueueOptions { Seats = 3 }), _h.Clock, Keeper());

    private WorkTask Queue(Desk desk, string title, string description = "In src/web/cart.ts.", TimeSpan? after = null)
    {
        var task = WorkTask.Assign(_h.Office, desk.Id, title, description, "elliot", _h.Clock.GetUtcNow());
        _h.Tasks.Add(task);
        _h.Clock.Advance(after ?? TimeSpan.FromSeconds(1));
        return task;
    }

    private void Done(Desk desk, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var task = WorkTask.Assign(_h.Office, desk.Id, $"Old {i}", null, "elliot", _h.Clock.GetUtcNow());
            _h.Tasks.Add(task);
            task.Start(_h.Clock.GetUtcNow());
            task.AttachRun(AgentRun.Create($"ses_{task.Key}", $"/tmp/{task.Key}", $"openspace/{task.Key}"), _h.Clock.GetUtcNow());
            task.SubmitForReview(_h.Clock.GetUtcNow());
            task.Accept(_h.Clock.GetUtcNow());
        }
    }

    [Fact]
    public void Only_queued_work_on_the_same_files_of_desks_in_at_the_same_time_clashes()
    {
        var a = Queue(_h.Desk, "Fix the total");
        var b = Queue(_screens, "Restyle the cart", "Colours of `src/web/cart.ts` and src/web/cart.css.");
        Queue(_screens, "Elsewhere", "Only docs/readme.md.");

        var clash = Assert.Single(Agreements.Clashes(_h.Office, _h.Tasks.Items));
        Assert.Equal((a.Id, b.Id, "src/web/cart.ts"), (clash.First.Id, clash.Second.Id, Agreements.Subject(clash.Shared)));

        // A night desk is never in with a day desk: their work never runs side by side.
        _h.Office.SetDeskShift(_h.Desk.Id, Shift.Day, _h.Clock.GetUtcNow());
        _h.Office.SetDeskShift(_screens.Id, Shift.Night, _h.Clock.GetUtcNow());
        Assert.Empty(Agreements.Clashes(_h.Office, _h.Tasks.Items));

        // An office that lets overlapping work run has nothing to agree on.
        _h.Office.SetDeskShift(_screens.Id, Shift.Always, _h.Clock.GetUtcNow());
        _h.Office.HoldOverlaps(false, _h.Clock.GetUtcNow());
        Assert.Empty(Agreements.Clashes(_h.Office, _h.Tasks.Items));
    }

    [Fact]
    public void Seniority_settles_it_first_then_priority_then_age_and_an_even_case_goes_to_the_person()
    {
        var older = Queue(_h.Desk, "Fix the total", after: TimeSpan.FromHours(2));
        var newer = Queue(_screens, "Restyle the cart");
        AgreementSide Side(Desk desk, WorkTask task, int done, int blocks) => new(desk.Id, desk.Name, task, done, blocks);

        var bySeniority = Agreements.Negotiate(Side(_h.Desk, older, 1, 0), Side(_screens, newer, 4, 0), TimeZoneInfo.Utc);
        var byPriority = Agreements.Negotiate(Side(_h.Desk, older, 2, 0), Side(_screens, newer, 2, 1), TimeZoneInfo.Utc);
        var byAge = Agreements.Negotiate(Side(_h.Desk, older, 2, 1), Side(_screens, newer, 2, 1), TimeZoneInfo.Utc);

        Assert.Equal((newer.Id, 2), (bySeniority.GoesFirst, bySeniority.Turns));
        Assert.StartsWith("Seniority: Screens finished 4 tasks", bySeniority.Reason, StringComparison.Ordinal);
        Assert.Equal((newer.Id, 3), (byPriority.GoesFirst, byPriority.Turns));
        Assert.Equal((older.Id, 4), (byAge.GoesFirst, byAge.Turns));
        Assert.Contains("rule 6", byAge.Reason, StringComparison.Ordinal);
        Assert.StartsWith("Frontend proposes to go first with “Fix the total”: 2 tasks finished, 1 task waiting for it", byAge.FirstProposal, StringComparison.Ordinal);

        var close = Queue(_h.Desk, "Close in time");
        var even = Agreements.Negotiate(Side(_h.Desk, newer, 2, 0), Side(_screens, close, 2, 0), TimeZoneInfo.Utc);
        Assert.Equal((null, Agreement.MaxTurns), (even.GoesFirst, even.Turns));
    }

    [Fact]
    public async Task A_settled_agreement_puts_the_senior_desks_work_first_and_writes_the_minutes()
    {
        Done(_screens, 2);
        var older = Queue(_h.Desk, "Fix the total");
        var newer = Queue(_screens, "Restyle the cart");
        _engines.Answer = "Screens went first, Frontend took it well.";

        var started = await Dispatcher().DispatchAsync(default);

        Assert.Equal([newer.Id], started);
        Assert.Equal("Waits for “Restyle the cart” to go first, as agreed with Screens.", _h.Waits.Snapshot().Single(w => w.TaskId == older.Id.Value).Reason);
        var agreement = Assert.Single(_agreements.Items);
        Assert.Equal((AgreementStatus.Settled, newer.Id), (agreement.Status, agreement.GoesFirst));
        Assert.Equal("Agreements/2015-06-24-frontend-and-screens-src-web-cart-ts.md", agreement.MinutesPath);
        var minutes = _h.Vault.Notes[agreement.MinutesPath];
        Assert.Equal(("agreement", "accepted", "Openspace"), (minutes.Kind, minutes.Status, minutes.Author));
        Assert.Contains("> Screens went first, Frontend took it well.", minutes.Body, StringComparison.Ordinal);
        Assert.Contains("## Proposals", minutes.Body, StringComparison.Ordinal);
        Assert.Contains("“Restyle the cart” goes first", minutes.Body, StringComparison.Ordinal);

        // A second round opens nothing new.
        await Dispatcher().DispatchAsync(default);
        Assert.Single(_agreements.Items);
    }

    [Fact]
    public async Task What_the_rules_do_not_settle_is_one_question_for_the_person_whose_answer_is_final()
    {
        var first = Queue(_h.Desk, "Fix the total");
        var second = Queue(_screens, "Restyle the cart");
        _journals = new() { Engine = Functions.ModelEngine.OpenCode, Model = "opencode/big-pickle-free" };

        Assert.Empty(await Dispatcher().DispatchAsync(default));

        var agreement = Assert.Single(_agreements.Items);
        Assert.Equal(AgreementStatus.WaitingForPerson, agreement.Status);
        Assert.Empty(_engines.Asked);
        Assert.Equal("open", _h.Vault.Notes[agreement.MinutesPath].Status);
        var needs = await new NeedsYouQueries(_h.Offices, _h.Tasks, _h.Routines, _h.Vault, _h.Clock, _agreements).ListAsync(default);
        var question = Assert.Single(needs, n => n.Kind == NeedsYouKind.Agreement);
        Assert.Equal("Who goes first on src/web/cart.ts?", question.Title);
        Assert.Contains("Frontend proposes", question.Detail, StringComparison.Ordinal);
        Assert.Contains("Screens proposes", question.Detail, StringComparison.Ordinal);

        await Keeper().DecideAsync(agreement.Id, second.Id, "elliot", default);

        Assert.Equal([second.Id], await Dispatcher().DispatchAsync(default));
        Assert.Equal(AgreementStatus.DecidedByPerson, agreement.Status);
        Assert.Contains("elliot decided: “Restyle the cart” goes first.", _h.Vault.Notes[agreement.MinutesPath].Body, StringComparison.Ordinal);
        await Assert.ThrowsAsync<RejectedException>(() => Keeper().DecideAsync(agreement.Id, first.Id, "elliot", default));
    }

    [Fact]
    public async Task A_question_whose_task_left_the_queue_is_dropped()
    {
        var first = Queue(_h.Desk, "Fix the total");
        Queue(_screens, "Restyle the cart");
        await Keeper().RoundAsync(default);

        first.Discard(_h.Clock.GetUtcNow());
        var round = await Keeper().RoundAsync(default);

        Assert.Equal(AgreementStatus.Dropped, Assert.Single(_agreements.Items).Status);
        Assert.Empty(round.Hold);
    }

    [Fact]
    public async Task Relationships_take_a_little_from_how_it_went()
    {
        Done(_screens, 1);
        Queue(_h.Desk, "Fix the total");
        Queue(_screens, "Restyle the cart");
        Queue(_h.Desk, "Tidy the cart", "Also src/web/cart.css and src/web/cart.ts.", TimeSpan.FromMinutes(1));
        await Keeper().RoundAsync(default);
        var names = _h.Office.Rooms.SelectMany(r => r.Desks).ToDictionary(d => d.Id, d => d.Name);

        var tie = Assert.Single(Relationships.Of(_h.Tasks.Items, [], names, _h.Clock.GetUtcNow(), _agreements.Items));

        Assert.Equal([TieKind.Agreed, TieKind.Agreed], tie.History.Select(h => h.Kind));
        Assert.Equal(2 * Relationships.AgreedChange, tie.Affinity);
    }

    private sealed class Engines : Functions.IEngineText
    {
        public string? Answer { get; set; }

        public List<string> Asked { get; } = [];

        public Task<string> AskAsync(Functions.ModelEngine engine, string? model, string instructions, string text, CancellationToken cancellationToken)
        {
            Asked.Add(text);
            return Answer is null ? throw new InvalidOperationException("No engine here") : Task.FromResult(Answer);
        }
    }

    private sealed class NoChat : LocalModels.ILocalChat
    {
        public Task<string> AskAsync(LocalModels.LocalAsk ask, CancellationToken cancellationToken) => throw new TimeoutException("The card is busy.");
    }
}
