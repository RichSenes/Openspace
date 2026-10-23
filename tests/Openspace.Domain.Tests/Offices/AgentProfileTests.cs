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

    [Fact]
    public void Desks_run_on_OpenCode_unless_told_otherwise()
    {
        Assert.Equal(AgentEngine.OpenCode, AgentProfile.Create("build").Engine);
        Assert.Equal(AgentEngine.ClaudeCode, AgentProfile.Create("build", null, AgentEngine.ClaudeCode).Engine);
    }

    [Theory]
    [InlineData("sonnet")]
    [InlineData("claude-opus-5-5")]
    [InlineData("opus[1m]")]
    public void Claude_Code_takes_its_own_model_names(string model)
    {
        Assert.Equal(model, AgentProfile.Create("build", model, AgentEngine.ClaudeCode).Model);
    }

    [Theory]
    [InlineData("anthropic/claude-sonnet")]
    [InlineData("two words")]
    public void Claude_Code_refuses_other_model_names(string model)
    {
        Assert.Throws<DomainException>(() => AgentProfile.Create("build", model, AgentEngine.ClaudeCode));
    }

    [Fact]
    public void OpenCode_still_wants_a_provider()
    {
        Assert.Throws<DomainException>(() => AgentProfile.Create("build", "sonnet"));
    }
}
