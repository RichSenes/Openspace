using System.Text.RegularExpressions;

namespace Openspace.Application.Agents;

/// <summary>
/// Says what an agent does in words a person reads at a glance: "Running the tests",
/// "Can I install the dependencies?". The command itself stays in the log and in the
/// permission's detail, never in a speech bubble.
/// </summary>
public static partial class ActionPhrases
{
    private sealed record Intent(string Doing, string Asking, string Done);

    private static readonly Intent Command = new("Running a command", "run a command", "Ran a command");
    private static readonly Intent Explore = new("Looking through the files", "look through the files", "Looked through the files");

    /// <summary>What a command is for, checked in order on each part of the command line.</summary>
    private static readonly (Regex Pattern, Intent Intent)[] Intents =
    [
        (TestPattern(), new("Running the tests", "run the tests", "Ran the tests")),
        (LintPattern(), new("Checking the code style", "check the code style", "Checked the code style")),
        (InstallPattern(), new("Installing the dependencies", "install the dependencies", "Installed the dependencies")),
        (DependenciesPattern(), new("Checking the dependencies", "check the dependencies", "Checked the dependencies")),
        (StartPattern(), new("Starting the app to try it", "start the app to try it", "Started the app")),
        (DatabasePattern(), new("Updating the database schema", "change the database schema", "Changed the database schema")),
        (BuildPattern(), new("Building the project", "build the project", "Built the project")),
        (GitPushPattern(), new("Pushing to the remote", "push to the remote", "Pushed to the remote")),
        (GitLookPattern(), new("Looking at the changes so far", "look at the changes in git", "Looked at the changes")),
        (GitChangePattern(), new("Tidying up the changes in git", "change things in git", "Changed things in git")),
        (DeployPattern(), new("Deploying", "deploy or publish", "Deployed or published")),
        (SystemPattern(), new("Changing the system", "change the machine's system", "Changed the system")),
        (RemotePattern(), new("Connecting to another machine", "connect to another machine", "Connected to another machine")),
        (DeletePattern(), new("Deleting files", "delete files", "Deleted files")),
        (FilesPattern(), new("Moving files around", "move files around", "Moved files around")),
        (WebPattern(), new("Fetching something from the web", "fetch something from the web", "Fetched something from the web")),
        (ScriptPattern(), new("Running a script", "run a script", "Ran a script")),
    ];

    /// <summary>For a bubble: "Running the tests", "Editing header.php".</summary>
    public static string Doing(AgentAction action) => Phrase(action).Doing;

    /// <summary>For a permission: "Can I run the tests?".</summary>
    public static string Asking(AgentAction action) => $"Can I {Phrase(action).Asking}?";

    /// <summary>For the log: "run the tests · npm test", what was asked and exactly what.</summary>
    public static string Request(AgentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var asking = Phrase(action).Asking;
        return action.Kind is ActionKind.Run or ActionKind.Fetch or ActionKind.OutsideFolder
            ? $"{asking} · {Shorten(action.Target, 160)}"
            : asking;
    }

    /// <summary>For the log: "Ran the tests"; a command nothing recognises keeps its own text.</summary>
    public static string Done(AgentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var intent = Phrase(action);
        return intent == Command ? $"Ran {Shorten(action.Target, 120)}" : intent.Done;
    }

    private static Intent Phrase(AgentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var file = FileName(action.Target);
        return action.Kind switch
        {
            ActionKind.Run => ForCommand(action.Target),
            ActionKind.Edit => new($"Editing {file}", $"edit {file}", $"Changed {file}"),
            ActionKind.Write => new($"Writing {file}", $"create {file}", $"Wrote {file}"),
            ActionKind.Fetch => new("Looking something up online", "look something up online", "Looked something up online"),
            ActionKind.OutsideFolder => new("Looking outside its working copy", "look outside its working copy", "Looked outside its working copy"),
            _ => new($"Using {action.Target}", $"use {action.Target}", $"Used {action.Target}"),
        };
    }

    /// <summary>The first part of the command line that says what it is for; mere looking around counts last.</summary>
    private static Intent ForCommand(string command)
    {
        var parts = Parts(command).ToList();
        foreach (var part in parts)
        {
            foreach (var (pattern, intent) in Intents)
            {
                if (pattern.IsMatch(part))
                {
                    return intent;
                }
            }
        }

        return parts.Any(p => ExplorePattern().IsMatch(p)) ? Explore : Command;
    }

    /// <summary>
    /// The commands of a command line, without what only sets them up: "cd src &amp;&amp; FOO=1 npm test"
    /// gives "npm test".
    /// </summary>
    internal static IEnumerable<string> Parts(string command) =>
        SeparatorPattern().Split(command ?? string.Empty)
            .Select(p => SetupPattern().Replace(p.Trim(), string.Empty))
            .Where(p => p.Length > 0 && !IgnoredPattern().IsMatch(p));

    private static string FileName(string target)
    {
        var name = Path.GetFileName(target.TrimEnd('/'));
        return name.Length > 0 ? name : "a file";
    }

    internal static string Shorten(string text, int max)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= max ? line : string.Concat(line.AsSpan(0, max - 1), "…");
    }

    [GeneratedRegex(@"&&|\|\||;|\|")]
    private static partial Regex SeparatorPattern();

    /// <summary>Environment variables and wrappers in front of a command.</summary>
    [GeneratedRegex(@"^((\w+=\S*|timeout\s+\S+|time|env|command)\s+)+")]
    private static partial Regex SetupPattern();

    [GeneratedRegex(@"^(cd|pushd|popd|export|set|source|\.|true|echo|printf)(\s|$)")]
    private static partial Regex IgnoredPattern();

    [GeneratedRegex(@"^((npm|pnpm|yarn|bun)\s+(run\s+)?test|(npx\s+)?(vitest|jest|mocha|playwright\s+test)|dotnet\s+test|(python3?\s+-m\s+)?pytest|(go|cargo)\s+test|(\./)?(vendor/bin/)?phpunit|(mvn|\./gradlew|gradle)\s.*test)\b")]
    private static partial Regex TestPattern();

    [GeneratedRegex(@"^((npm|pnpm|yarn|bun)\s+run\s+(lint|format|typecheck|check)|(npx\s+)?(eslint|prettier|tsc|stylelint)|dotnet\s+format|ruff|flake8|black|mypy|php\s+-l)\b")]
    private static partial Regex LintPattern();

    [GeneratedRegex(@"^((npm|pnpm|bun)\s+(ci|install|i|add|update)|yarn(\s+(install|add|upgrade))?$|yarn\s+(install|add|upgrade)|dotnet\s+restore|dotnet\s+add\s.*package|(pip3?|python3?\s+-m\s+pip)\s+install|composer\s+(install|update|require)|bundle\s+install)\b")]
    private static partial Regex InstallPattern();

    [GeneratedRegex(@"^((npm|pnpm|yarn)\s+(audit|outdated|ls|why)|dotnet\s+list\s.*package|dotnet\s+(nuget\s+)?why|composer\s+(audit|outdated|show)|pip3?\s+(list|show))\b")]
    private static partial Regex DependenciesPattern();

    [GeneratedRegex(@"^((npm|pnpm|yarn|bun)\s+(run\s+)?(dev|start|serve|preview)|dotnet\s+(run|watch))\b")]
    private static partial Regex StartPattern();

    [GeneratedRegex(@"^(dotnet\s+ef|(npx\s+)?prisma\s+(migrate|db))\b")]
    private static partial Regex DatabasePattern();

    [GeneratedRegex(@"^((npm|pnpm|yarn|bun)\s+run\s+build|dotnet\s+(build|publish)|(cargo|go)\s+build|make|(npx\s+)?(vite|next)\s+build|mvn|\./gradlew|gradle)\b")]
    private static partial Regex BuildPattern();

    [GeneratedRegex(@"^git\s+push\b")]
    internal static partial Regex GitPushPattern();

    [GeneratedRegex(@"^git\s+(status|diff|log|show|branch|blame|ls-files|rev-parse|grep)\b")]
    private static partial Regex GitLookPattern();

    [GeneratedRegex(@"^git\s")]
    private static partial Regex GitChangePattern();

    /// <summary>Anything that reaches a live service: deploys, releases, packages published.</summary>
    [GeneratedRegex(@"^((npx\s+)?(wrangler|vercel|netlify|firebase|flyctl|fly|heroku|supabase)|(npm|pnpm|yarn)\s+publish|dotnet\s+nuget\s+push|docker\s+push|gh|twine\s+upload|terraform\s+apply|kubectl)\b")]
    internal static partial Regex DeployPattern();

    [GeneratedRegex(@"^(sudo|su|apt|apt-get|dnf|yum|brew|snap|systemctl|service|chown|kill|pkill|killall|shutdown|reboot|mkfs|dd|crontab)\b")]
    internal static partial Regex SystemPattern();

    [GeneratedRegex(@"^(ssh|scp|sftp|ftp|rsync)\b")]
    internal static partial Regex RemotePattern();

    [GeneratedRegex(@"^(rm|rmdir|unlink)\b")]
    internal static partial Regex DeletePattern();

    [GeneratedRegex(@"^(mkdir|cp|mv|touch|chmod|ln)\b")]
    private static partial Regex FilesPattern();

    [GeneratedRegex(@"^(curl|wget|http)\b")]
    internal static partial Regex WebPattern();

    [GeneratedRegex(@"^(node|python3?|deno|bun|tsx|ts-node|php|ruby|bash|sh|npx|(npm|pnpm|yarn)\s+run|\./)")]
    private static partial Regex ScriptPattern();

    [GeneratedRegex(@"^(ls|cat|head|tail|sed|grep|egrep|rg|find|fd|wc|tree|awk|pwd|which|file|stat|du|diff|less|sort|uniq|cut|jq|realpath|readlink|nl|xxd)\b")]
    private static partial Regex ExplorePattern();
}
