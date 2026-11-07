using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.Work;

namespace Openspace.Application.Tests;

public class SupervisorJobsTests
{
    private readonly Harness _h = new();

    private SupervisorJobs Jobs() => new(new NoServices(), _h.Publisher, _h.Clock, NullLogger<SupervisorJobs>.Instance);

    /// <summary>Plans in these tests ask for no services: a scope with nothing in it.</summary>
    private sealed class NoServices : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;

        public IServiceScope CreateScope() => this;

        public object? GetService(Type serviceType) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task A_plan_runs_in_the_background_and_is_shown_while_it_runs_and_when_done()
    {
        var jobs = Jobs();
        var gate = new TaskCompletionSource<SupervisorOutcome>();
        var epic = Guid.NewGuid();

        var job = jobs.Start(_h.Office.Id.Value, "plan", "Add a contact page.\nWith a form.", "plan:1", (_, _) => gate.Task);
        var meanwhile = jobs.Snapshot(_h.Office.Id.Value);
        gate.SetResult(new SupervisorOutcome(3, epic));
        await jobs.Completion(job.Id);

        Assert.Equal((SupervisorJobState.Planning, "Add a contact page. With a form."), (meanwhile.Single().State, meanwhile.Single().What));
        var done = Assert.Single(jobs.Snapshot());
        Assert.Equal((SupervisorJobState.Done, 3, (Guid?)epic), (done.State, done.Tasks, done.EpicId));
        Assert.Equal(SupervisorJobState.Done, _h.Publisher.Jobs[^1].Single().State);
    }

    [Fact]
    public async Task The_same_request_while_one_runs_gets_the_running_plan_and_a_refusal_is_shown_as_failed()
    {
        var jobs = Jobs();
        var gate = new TaskCompletionSource<SupervisorOutcome>();

        var first = jobs.Start(_h.Office.Id.Value, "security", "Run 1", "security:1", (_, _) => gate.Task);
        var second = jobs.Start(_h.Office.Id.Value, "security", "Run 1", "security:1", (_, _) => Task.FromResult(new SupervisorOutcome(9, null)));
        var refused = jobs.Start(_h.Office.Id.Value, "plan", "Something", "plan:2", (_, _) => throw new RejectedException("no-desks", "No desks yet."));
        gate.SetResult(new SupervisorOutcome(1, null));
        await jobs.Completion(first.Id);
        await jobs.Completion(refused.Id);

        Assert.Equal(first.Id, second.Id);
        var failed = jobs.Snapshot().Single(j => j.Id == refused.Id);
        Assert.Equal((SupervisorJobState.Failed, "No desks yet."), (failed.State, failed.Error));
    }

    [Fact]
    public async Task Finished_plans_stay_on_show_for_a_quarter_of_an_hour()
    {
        var jobs = Jobs();
        var job = jobs.Start(_h.Office.Id.Value, "plan", "Logout", "plan:3", (_, _) => Task.FromResult(new SupervisorOutcome(1, null)));
        await jobs.Completion(job.Id);

        _h.Clock.Advance(TimeSpan.FromMinutes(14));
        var still = jobs.Snapshot().Count;
        _h.Clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(1, still);
        Assert.Empty(jobs.Snapshot());
    }
}
