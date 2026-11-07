using Openspace.Domain.Common;
using Openspace.Domain.Roles;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Roles;

public class RoleSkillsTests
{
    [Fact]
    public void A_role_gives_every_skill_until_it_names_some()
    {
        var role = Role.Define("Tester", null, null, null, RolePermissions.Default, Now);

        Assert.False(role.SkillsChosen);
        Assert.Null(role.Skills);
    }

    [Fact]
    public void Named_skills_are_trimmed_kept_once_and_sorted_and_null_gives_them_all_back()
    {
        var role = Role.Define("Auditor", null, null, null, RolePermissions.Default, Now);

        role.UseSkills([" tdd ", "security-audit", "tdd"], Now);
        var named = role.Skills;
        role.UseSkills(null, Now);

        Assert.Equal(["security-audit", "tdd"], named);
        Assert.Null(role.Skills);
        Assert.Equal(2, role.DomainEvents.OfType<RoleChanged>().Count());
    }

    [Fact]
    public void An_empty_list_gives_no_skill_at_all()
    {
        var role = Role.Define("Writer", null, null, null, RolePermissions.Default, Now);

        role.UseSkills([], Now);

        Assert.True(role.SkillsChosen);
        Assert.Empty(role.Skills!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secrets")]
    [InlineData("two words")]
    public void Skill_names_are_plain_names(string name)
    {
        var role = Role.Define("Tester", null, null, null, RolePermissions.Default, Now);

        Assert.Throws<DomainException>(() => role.UseSkills([name], Now));
    }
}
