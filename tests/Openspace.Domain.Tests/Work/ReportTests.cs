using Openspace.Domain.Common;
using Openspace.Domain.Reports;
using Openspace.Domain.Work;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Work;

public class ReportTests
{
    [Fact]
    public void A_period_report_keeps_its_period_and_raises_an_event()
    {
        var office = ECorp();

        var report = Report.ForPeriod(office, Now, Now.AddDays(1), "24 June", "# Done", true, "elliot", Now);

        Assert.Equal((ReportKind.Period, (DateTimeOffset?)Now, (DateTimeOffset?)Now.AddDays(1)), (report.Kind, report.From, report.To));
        Assert.IsType<ReportWritten>(Assert.Single(report.DomainEvents));
    }

    [Fact]
    public void A_period_must_end_after_it_starts_and_a_report_needs_a_body()
    {
        Assert.Throws<DomainException>(() => Report.ForPeriod(ECorp(), Now, Now, "Empty", "# x", true, "elliot", Now));
        Assert.Throws<DomainException>(() => Report.ForPeriod(ECorp(), Now, Now.AddDays(1), "Day", " ", true, "elliot", Now));
    }

    [Fact]
    public void An_epic_report_belongs_to_the_epic_office()
    {
        var office = ECorp();
        var epic = Epic.Open(office, "Contact page", "Add a contact page.", "elliot", Now);

        var report = Report.ForEpic(epic, "Contact page", "# Status", false, "elliot", Now);

        Assert.Equal((office.Id, (EpicId?)epic.Id, false), (report.OfficeId, report.EpicId, report.WrittenByAi));
    }
}
