using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Work;

public class AgreementTests
{
    private static readonly OfficeId Office = OfficeId.New();
    private static readonly (DeskId A, DeskId B) Desks = (DeskId.New(), DeskId.New());
    private static readonly (WorkTaskId A, WorkTaskId B) Tasks = (WorkTaskId.New(), WorkTaskId.New());

    private static Agreement Open() => Agreement.Open(Office, Desks.A, Tasks.A, Desks.B, Tasks.B, "src/web/cart.ts", Now);

    [Fact]
    public void Is_between_two_desks_on_two_tasks()
    {
        Assert.Throws<DomainException>(() => Agreement.Open(Office, Desks.A, Tasks.A, Desks.A, Tasks.B, "x", Now));
        Assert.Throws<DomainException>(() => Agreement.Open(Office, Desks.A, Tasks.A, Desks.B, Tasks.A, "x", Now));
    }

    [Fact]
    public void Settled_by_the_rules_it_is_closed_and_puts_the_other_task_second()
    {
        var agreement = Open();
        agreement.Record("A first.", "B first.", 2, Tasks.B, "Seniority.", "Agreements/x.md", Now);

        Assert.Equal((AgreementStatus.Settled, Tasks.A, (DateTimeOffset?)Now), (agreement.Status, agreement.GoesSecond, agreement.ClosedAt));
        Assert.Throws<DomainException>(() => agreement.Decide(Tasks.A, "elliot", Now));
    }

    [Fact]
    public void Keeps_to_its_turns_and_its_two_tasks()
    {
        Assert.Throws<DomainException>(() => Open().Record("A.", "B.", Agreement.MaxTurns + 1, null, "Too long.", "x.md", Now));
        Assert.Throws<DomainException>(() => Open().Record("A.", "B.", 2, WorkTaskId.New(), "Who?", "x.md", Now));
    }

    [Fact]
    public void Waiting_for_the_person_it_takes_their_answer_or_is_dropped()
    {
        var answered = Open();
        answered.Record("A.", "B.", Agreement.MaxTurns, null, "Even.", "x.md", Now);
        Assert.Equal(AgreementStatus.WaitingForPerson, answered.Status);
        Assert.Throws<DomainException>(() => answered.Decide(WorkTaskId.New(), "elliot", Now));
        answered.Decide(Tasks.A, "elliot", Now);
        answered.Drop(Now);
        Assert.Equal((AgreementStatus.DecidedByPerson, "elliot"), (answered.Status, answered.DecidedBy));

        var dropped = Open();
        dropped.Record("A.", "B.", Agreement.MaxTurns, null, "Even.", "x.md", Now);
        dropped.Drop(Now);
        Assert.Equal(AgreementStatus.Dropped, dropped.Status);
    }
}
