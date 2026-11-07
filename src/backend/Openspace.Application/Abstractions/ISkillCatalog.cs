namespace Openspace.Application.Abstractions;

/// <summary>A skill installed on this machine that agents can load.</summary>
public sealed record SkillInfo(string Name, string Description);

/// <summary>The skills installed for the agents, read from their folder.</summary>
public interface ISkillCatalog
{
    /// <summary>The folder the skills are read from, or null when there is none.</summary>
    string? Folder { get; }

    /// <summary>Every installed skill, by name.</summary>
    IReadOnlyList<SkillInfo> List();
}
