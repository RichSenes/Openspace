using Openspace.Domain.Offices;

namespace Openspace.Domain.Tests;

/// <summary>Made-up fixtures. Any resemblance to a certain conglomerate is entirely intentional.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);

    public static Office ECorp() => Office.Open("E Corp", Now);

    public static (Office Office, Desk Desk) ECorpWithDesk()
    {
        var office = ECorp();
        var room = office.OpenRoom("Web", Now);
        var desk = office.AddDesk(room.Id, "Frontend", AgentProfile.Create("frontend-dev"), Now);
        return (office, desk);
    }
}
