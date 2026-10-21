namespace Openspace.Api;

/// <summary>The person using this instance. There is no login yet: one operator per instance.</summary>
public sealed class OperatorOptions
{
    public const string SectionName = "Operator";

    public string Name { get; set; } = "operator";
}
