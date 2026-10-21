using System.Reflection;
using NetArchTest.Rules;

namespace Openspace.ArchitectureTests;

/// <summary>
/// Enforces the Clean Architecture dependency rule: dependencies point inwards only.
/// </summary>
public class LayerDependencyTests
{
    private const string Domain = "Openspace.Domain";
    private const string Application = "Openspace.Application";
    private const string Infrastructure = "Openspace.Infrastructure";
    private const string Api = "Openspace.Api";

    private static readonly Assembly DomainAssembly = typeof(Domain.AssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.AssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_depends_on_no_other_layer()
    {
        AssertNoDependency(DomainAssembly, Application, Infrastructure, Api);
    }

    [Fact]
    public void Application_depends_only_on_domain()
    {
        AssertNoDependency(ApplicationAssembly, Infrastructure, Api);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        AssertNoDependency(InfrastructureAssembly, Api);
    }

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Forbidden dependencies in: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
