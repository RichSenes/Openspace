using Openspace.Domain.Common;
using Openspace.Domain.Offices;

namespace Openspace.Domain.Tests.Offices;

public class AgentProfileTests
{
    [Theory]
    [InlineData("frontend-dev")]
    [InlineData("nis2")]
    [InlineData("keycloak-admin-2")]
    public void Accepts_lowercase_dashed_names(string name)
    {
        Assert.Equal(name, AgentProfile.Create(name).AgentName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Frontend")]
    [InlineData("front end")]
    [InlineData("-dev")]
    [InlineData("dev-")]
    [InlineData("front--end")]
    public void Rejects_invalid_names(string name)
    {
        Assert.Throws<DomainException>(() => AgentProfile.Create(name));
    }

    [Fact]
    public void Model_is_optional()
    {
        Assert.Null(AgentProfile.Create("tester").Model);
        Assert.Null(AgentProfile.Create("tester", "  ").Model);
    }

    [Fact]
    public void Model_must_name_a_provider()
    {
        Assert.Equal("local/tiny-model", AgentProfile.Create("tester", " local/tiny-model ").Model);
        Assert.Throws<DomainException>(() => AgentProfile.Create("tester", "tiny-model"));
    }

    [Fact]
    public void Profiles_with_same_values_are_equal()
    {
        Assert.Equal(AgentProfile.Create("tester", "local/a"), AgentProfile.Create("tester", "local/a"));
    }
}
