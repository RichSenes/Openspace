using System.Text.RegularExpressions;

namespace Openspace.Application.Agents;

public enum Autonomy
{
    /// <summary>Openspace answers on its own and asks a person only about risky actions.</summary>
    Auto,

    /// <summary>A person answers every request.</summary>
    Ask,
}

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    /// <summary>Who answers what agents ask to do, beyond what their role allows outright.</summary>
    public Autonomy Autonomy { get; set; } = Autonomy.Auto;
}

/// <summary>
/// Which requests still need a person when Openspace answers on its own: whatever leaves the
/// task's working copy, reaches a live service, changes the machine, deletes outside the
/// copy or touches secrets. Everything else stays inside a private working copy that a
/// person reviews before it goes anywhere.
/// </summary>
public static partial class PermissionPolicy
{
    /// <summary>Folders outside the working copy that commands may name without asking.</summary>
    private static readonly string[] HarmlessRoots = ["/dev/", "/tmp/", "/usr/", "/bin/", "/proc/"];

    /// <returns>Why a person must decide, or null when Openspace may allow it.</returns>
    public static string? NeedsPerson(AgentAction action, string workspacePath)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.Kind switch
        {
            ActionKind.OutsideFolder => "it reaches outside the task's working copy",
            ActionKind.Edit or ActionKind.Write => FileRisk(action.Target, workspacePath),
            ActionKind.Run => CommandRisk(action.Target, workspacePath),
            _ => null,
        };
    }

    private static string? FileRisk(string path, string workspacePath)
    {
        if (SecretPattern().IsMatch(path))
        {
            return "it touches a file that may hold secrets";
        }

        return Path.IsPathRooted(path) && !Inside(path, workspacePath) ? "it changes a file outside the task's working copy" : null;
    }

    private static string? CommandRisk(string command, string workspacePath)
    {
        if (SecretPattern().IsMatch(command))
        {
            return "it touches a file that may hold secrets";
        }

        if (HomePattern().IsMatch(command)
            || AbsolutePathPattern().Matches(command).Any(m => !Inside(m.Value, workspacePath) && !HarmlessRoots.Any(r => m.Value.StartsWith(r, StringComparison.Ordinal))))
        {
            return "it reaches outside the task's working copy";
        }

        if (WebPipePattern().IsMatch(command))
        {
            return "it runs a script downloaded from the web";
        }

        foreach (var part in ActionPhrases.Parts(command))
        {
            if (ActionPhrases.GitPushPattern().IsMatch(part) || GitRiskPattern().IsMatch(part))
            {
                return "it changes the repository beyond the working copy";
            }

            if (ActionPhrases.DeployPattern().IsMatch(part))
            {
                return "it reaches a live service";
            }

            if (ActionPhrases.SystemPattern().IsMatch(part) || ActionPhrases.RemotePattern().IsMatch(part))
            {
                return "it changes the machine or another one";
            }

            if (ActionPhrases.WebPattern().IsMatch(part) && WebWritePattern().IsMatch(part))
            {
                return "it sends data to the web";
            }

            if (ActionPhrases.DeletePattern().IsMatch(part) && WideDeletePattern().IsMatch(part))
            {
                return "it deletes files that may lie outside the working copy";
            }
        }

        return null;
    }

    private static bool Inside(string path, string workspacePath)
    {
        var root = workspacePath.TrimEnd('/') + "/";
        return path.StartsWith(root, StringComparison.Ordinal) || path == workspacePath.TrimEnd('/');
    }

    /// <summary>Environment files (not their examples), keys, keystores and credentials.</summary>
    [GeneratedRegex(@"(^|[\s/'""=])\.env(\.(?!example|sample|template)[\w-]+)?($|[\s'""])|\.pem\b|\.p12\b|\.keystore\b|\.jks\b|id_rsa|id_ed25519|\.ssh/|credentials\.json|\.npmrc\b|\.netrc\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"(^|[\s='""])(~|\$HOME|\$\{HOME\})(/|\s|$)")]
    private static partial Regex HomePattern();

    /// <summary>Absolute paths: after a space, a quote or an equals sign, so "a/b" and "2>/dev/null" read right.</summary>
    [GeneratedRegex(@"(?<=(^|[\s'""=>]))/[^\s'""|;&)]+")]
    private static partial Regex AbsolutePathPattern();

    [GeneratedRegex(@"(curl|wget)[^|]*\|\s*(sudo\s+)?(ba|z)?sh\b")]
    private static partial Regex WebPipePattern();

    [GeneratedRegex(@"\s(-X\s*(POST|PUT|PATCH|DELETE)|--request\s+(POST|PUT|PATCH|DELETE)|-d|--data\S*|-F|--form|-T|--upload-file|--post-data|--post-file)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WebWritePattern();

    [GeneratedRegex(@"^git\s+(reset\s+--hard|clean\s+-\w*f|branch\s+-D|remote\s+(add|set-url|remove|rm)|config\s+--global|worktree|filter-branch)\b")]
    private static partial Regex GitRiskPattern();

    /// <summary>A recursive delete of something above the working copy, or of a variable nobody can see.</summary>
    [GeneratedRegex(@"\s-\w*[rR]\w*\b.*\s(\.\.|\$)")]
    private static partial Regex WideDeletePattern();
}
