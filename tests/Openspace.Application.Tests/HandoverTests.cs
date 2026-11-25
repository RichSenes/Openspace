using Openspace.Application.Attention;
using Openspace.Application.Agents;
using Openspace.Application.Characters;
using Openspace.Application.Queue;
using Openspace.Application.World;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>Hand-overs between desks and meetings in the world, on a made-up office.</summary>
public sealed class HandoverTests
{
    private readonly Harness _h = new();
    private readonly InMemoryAgreements _agreements = new();
    private readonly Desk _screens;

    public HandoverTests() => _screens = _h.Office.AddDesk(_h.Room.Id, "Screens", AgentProfile.Create("designer"), Harness.Start);

    private WorkTask Finished(Desk desk, string title)
    {
        var task = WorkTask.Assign(_h.Office, desk.Id, title, "The cart's total, in src/web/cart.ts.", "elliot", _h.Clock.GetUtcNow());
        _h.Tasks.Add(task);
        task.Start(_h.Clock.GetUtcNow());
        task.SubmitForReview(_h.Clock.GetUtcNow());
        task.Accept(_h.Clock.GetUtcNow());
        return task;
    }

    private WorkTask Waiting(Desk desk, string title, params WorkTask[] after)
    {
        var task = WorkTask.Assign(_h.Office, desk.Id, title, "Restyle the cart.", "elliot", _h.Clock.GetUtcNow());
        task.DependOn(after, _h.Clock.GetUtcNow());
        _h.Tasks.Add(task);
        return task;
    }

    private QueueDispatcher Dispatcher() =>
        new(_h.Tasks, _h.Offices, _h.Limits, _h.Gpu, _h.Waits, _h.Work, _h.UnitOfWork, new LiveOptions<QueueOptions>(() => new QueueOptions { Seats = 3 }), _h.Clock, null,
            new HandoverKeeper(_h.Offices, _h.Tasks, _h.Vault, _h.Clock));

    private WorldQueries World() =>
        new(_h.Offices, _h.Tasks, _h.Routines, _h.Activity, _h.Interns, new NeedsYouQueries(_h.Offices, _h.Tasks, _h.Routines, new InMemoryVault(), _h.Clock), _h.Clock, _agreements);

    [Fact]
    public void A_hand_over_is_fresh_work_of_one_desk_that_another_desks_task_waits_for()
    {
        var total = Finished(_h.Desk, "Fix the total");
        var mine = Finished(_screens, "Pick the colours");
        var next = Waiting(_screens, "Restyle the cart", total, mine);
        _h.Clock.Advance(TimeSpan.FromMinutes(5));

        var handover = Assert.Single(Handovers.Recent(_h.Tasks.Items, _h.Clock.GetUtcNow()));

        // Its own desk's work is no hand-over; neither is work done long ago.
        Assert.Equal((total.Id, next.Id), (handover.Done.Id, handover.Next.Id));
        _h.Clock.Advance(Handovers.Fresh);
        Assert.Empty(Handovers.Recent(_h.Tasks.Items, _h.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task The_queue_writes_the_hand_overs_note_once_linked_to_both_tasks()
    {
        var total = Finished(_h.Desk, "Fix the total");
        var next = Waiting(_screens, "Restyle the cart", total);
        _h.Clock.Advance(TimeSpan.FromMinutes(1));

        await Dispatcher().DispatchAsync(default);
        await Dispatcher().DispatchAsync(default);

        var note = Assert.Single(_h.Vault.Notes.Values, n => n.Kind == "handover");
        Assert.StartsWith("Handovers/2015-06-24-frontend-to-screens-restyle-the-cart-", note.Path, StringComparison.Ordinal);
        Assert.Equal("Frontend hands “Fix the total” to Screens", note.Title);
        Assert.Equal($"tasks {total.Id.Value} {next.Id.Value}", note.From);
        Assert.Contains("## Done", note.Body, StringComparison.Ordinal);
        Assert.Contains("“Restyle the cart”, for Screens", note.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_world_shows_who_meets_whom_about_a_hand_over_or_an_agreement_for_a_while()
    {
        var total = Finished(_h.Desk, "Fix the total");
        var next = Waiting(_screens, "Restyle the cart", total);
        var agreement = Agreement.Open(_h.Office.Id, _h.Desk.Id, total.Id, _screens.Id, next.Id, "src/web/cart.ts", _h.Clock.GetUtcNow());
        _agreements.Add(agreement);
        _h.Clock.Advance(TimeSpan.FromMinutes(2));

        var meetings = (await World().GetAsync(default)).Meetings;

        Assert.Equal(["Agreement", "Handover"], meetings.Select(m => m.Kind));
        var handover = meetings[1];
        Assert.Equal((_h.Desk.Id.Value, _screens.Id.Value, next.Id.Value), (handover.FirstCharacterId, handover.SecondCharacterId, handover.Id));
        Assert.Equal("“Fix the total” is done: “Restyle the cart” can go ahead", handover.Subject);
        Assert.StartsWith("Handovers/", handover.NotePath, StringComparison.Ordinal);
        Assert.Equal("src/web/cart.ts", meetings[0].Subject);

        _h.Clock.Advance(Handovers.Fresh);
        Assert.Empty((await World().GetAsync(default)).Meetings);
    }
}
