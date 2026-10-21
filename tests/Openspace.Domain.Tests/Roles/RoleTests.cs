using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Roles;

public class RoleTests
{
    private static readonly RolePermissions Careful = new(PermissionLevel.Ask, PermissionLevel.Deny, PermissionLevel.Deny);

    [Fact]
    public void A_role_is_defined_with_trimmed_text_and_raises_an_event()
    {
        var role = Role.Define("  Security auditor ", " Reviews changes ", " Never change code. ", " opencode/small ", Careful, Now);

        Assert.Equal(("Security auditor", "Reviews changes", "Never change code.", "opencode/small"),
            (role.Name, role.Description, role.Instructions, role.Model));
        Assert.Equal(Careful, role.Permissions);
        Assert.IsType<RoleDefined>(Assert.Single(role.DomainEvents));
    }

    [Fact]
    public void Description_instructions_and_model_are_optional()
    {
        var role = Role.Define("Tester", null, null, null, RolePermissions.Default, Now);

        Assert.Equal((string.Empty, string.Empty, (string?)null), (role.Description, role.Instructions, role.Model));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("Tester", "small")]
    [InlineData("Tester", "/small")]
    [InlineData("Tester", "opencode/")]
    public void Invalid_names_and_models_are_refused(string name, string? model)
    {
        Assert.Throws<DomainException>(() => Role.Define(name, null, null, model, RolePermissions.Default, Now));
    }

    [Fact]
    public void Changing_a_role_replaces_everything_and_raises_an_event()
    {
        var role = Role.Define("Tester", null, "Write tests.", null, RolePermissions.Default, Now);

        role.Change("QA", "Checks quality", "Write and run tests.", "opencode/big", Careful, Now);

        Assert.Equal(("QA", "opencode/big", Careful), (role.Name, role.Model, role.Permissions));
        Assert.IsType<RoleChanged>(role.DomainEvents[^1]);
    }

    [Fact]
    public void Desks_can_take_a_role_and_drop_it()
    {
        var (office, desk) = ECorpWithDesk();
        var role = RoleId.New();

        office.SetDeskRole(desk.Id, role, Now);
        var uses = office.UsesRole(role);
        office.SetDeskRole(desk.Id, null, Now);

        Assert.True(uses);
        Assert.Null(desk.RoleId);
        Assert.IsType<DeskRoleChanged>(office.DomainEvents[^1]);
    }

    [Fact]
    public void A_desk_can_get_its_role_when_it_is_added()
    {
        var office = ECorp();
        var room = office.OpenRoom("Web", Now);
        var role = RoleId.New();

        var desk = office.AddDesk(room.Id, "Frontend", AgentProfile.Create("build"), Now, role);

        Assert.Equal(role, desk.RoleId);
    }
}
