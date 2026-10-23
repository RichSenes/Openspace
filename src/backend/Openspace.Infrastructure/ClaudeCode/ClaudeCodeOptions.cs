namespace Openspace.Infrastructure.ClaudeCode;

public sealed class ClaudeCodeOptions
{
    public const string SectionName = "ClaudeCode";

    /// <summary>The Claude Code command; it runs with the account it is signed in with.</summary>
    public string Command { get; set; } = "claude";

    /// <summary>Off to never use Claude Code, even where it is installed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Claude Code plans and drafts instead of OpenCode, when it is there.</summary>
    public bool Supervise { get; set; } = true;

    /// <summary>Claude Code reviews finished work before a person does, when it is there.</summary>
    public bool Review { get; set; } = true;

    /// <summary>Model for plans and drafts; null means Claude Code's default.</summary>
    public string? SupervisorModel { get; set; }

    /// <summary>Model for reviews; null means Claude Code's default.</summary>
    public string? ReviewModel { get; set; }

    /// <summary>Longest a plan, a draft or a review may take.</summary>
    public TimeSpan AskTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
