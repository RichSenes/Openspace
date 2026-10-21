namespace Openspace.Infrastructure.Git;

public sealed class WorkspaceOptions
{
    public const string SectionName = "Workspaces";

    /// <summary>Folder that holds one working copy per task. Must be absolute.</summary>
    public string Root { get; set; } = string.Empty;
}
