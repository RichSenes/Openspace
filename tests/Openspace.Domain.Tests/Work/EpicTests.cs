using Openspace.Domain.Common;
using Openspace.Domain.Work;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Work;

public class EpicTests
{
    [Fact]
    public void An_epic_keeps_the_request_as_asked()
    {
        var office = ECorp();

        var epic = Epic.Open(office, " Contact page ", "Add a contact page that emails the admin.", "elliot", Now);

        Assert.Equal(("Contact page", "Add a contact page that emails the admin.", office.Id), (epic.Title, epic.Request, epic.OfficeId));
        Assert.IsType<EpicOpened>(Assert.Single(epic.DomainEvents));
    }

    [Fact]
    public void An_epic_needs_a_title_and_a_request()
    {
        Assert.Throws<DomainException>(() => Epic.Open(ECorp(), " ", "Something", "elliot", Now));
        Assert.Throws<DomainException>(() => Epic.Open(ECorp(), "Title", "", "elliot", Now));
    }

    [Fact]
    public void Proposed_tasks_belong_to_an_epic_of_their_own_office_only()
    {
        var office = ECorp();
        var epic = Epic.Open(office, "Contact page", "Add a contact page.", "elliot", Now);

        var task = WorkTask.Propose(office, "Build the form", null, "supervisor", Now, epic);

        Assert.Equal(epic.Id, task.EpicId);
        Assert.Throws<DomainException>(() => WorkTask.Propose(ECorp(), "Elsewhere", null, "supervisor", Now, epic));
    }
}
