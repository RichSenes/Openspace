namespace Openspace.Infrastructure.Skills;

public sealed class SkillsOptions
{
    public const string SectionName = "Skills";

    /// <summary>
    /// The folder of installed skills, one folder with a SKILL.md each; blank gives the agents
    /// none. OpenCode reads <c>~/.claude/skills</c> on its own as well.
    /// </summary>
    public string Folder { get; set; } = "~/.claude/skills";

    /// <summary>Where Openspace keeps the small read-only plugins that hand skills to Claude Code.</summary>
    public string PluginsFolder { get; set; } = "~/.local/share/openspace/skill-plugins";
}
