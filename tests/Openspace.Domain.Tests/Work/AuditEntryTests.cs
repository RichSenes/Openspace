using Openspace.Domain.Common;
using Openspace.Domain.Work;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Work;

public class AuditEntryTests
{
    [Fact]
    public void Huge_details_are_cut_and_say_so()
    {
        var entry = AuditEntry.Record(WorkTaskId.New(), Now, AuditKind.Command, "Ran npm test", new string('x', 50_000));

        Assert.True(entry.Detail!.Length < 16_100);
        Assert.EndsWith("(cut)", entry.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_details_are_dropped_and_a_summary_is_required()
    {
        Assert.Null(AuditEntry.Record(WorkTaskId.New(), Now, AuditKind.Read, "Read a file", "  ").Detail);
        Assert.Throws<DomainException>(() => AuditEntry.Record(WorkTaskId.New(), Now, AuditKind.Read, " "));
    }
}
