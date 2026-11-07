using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Openspace.Application.Agents;
using Openspace.Application.Abstractions;
using Openspace.Domain.Roles;
using Openspace.Infrastructure.ClaudeCode;
using Openspace.Infrastructure.OpenCode;
using Openspace.Infrastructure.Skills;

namespace Openspace.Infrastructure.Tests;

public sealed class SkillsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "openspace-skills-" + Guid.NewGuid().ToString("N"));

    private string Skills => Path.Combine(_root, "skills");

    private SkillCatalog Catalog(string? folder = null) => new(new LiveOptions<SkillsOptions>(new SkillsOptions
    {
        Folder = folder ?? Skills,
        PluginsFolder = Path.Combine(_root, "plugins"),
    }));

    public SkillsTests()
    {
        Write("security-audit", "---\nname: security-audit\ndescription: \"Audits code for holes.\"\n---\n# Security Audit\n");
        Write("tdd", "---\nname: tdd\ndescription: Test first.\n---\n");
        Write("no-front-matter", "# Just a title\n");
        Directory.CreateDirectory(Path.Combine(Skills, "not-a-skill"));
    }

    private void Write(string name, string text)
    {
        Directory.CreateDirectory(Path.Combine(Skills, name));
        File.WriteAllText(Path.Combine(Skills, name, "SKILL.md"), text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            // Links first, so deleting never walks into the skills they point at twice.
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void The_catalog_lists_folders_with_a_skill_file_by_their_front_matter()
    {
        var skills = Catalog().List();

        Assert.Equal(["no-front-matter", "security-audit", "tdd"], skills.Select(s => s.Name));
        Assert.Equal("Audits code for holes.", skills.Single(s => s.Name == "security-audit").Description);
        Assert.Empty(Catalog(Path.Combine(_root, "missing")).List());
    }

    [Fact]
    public void Every_skill_is_one_link_to_the_folder_and_named_ones_are_links_each()
    {
        var all = Catalog().For(null)!;
        var some = Catalog().For(["security-audit", "gone"])!;

        Assert.Equal(Skills, new DirectoryInfo(Path.Combine(all.PluginFolder, "skills")).LinkTarget);
        Assert.Equal(["security-audit"], Directory.EnumerateDirectories(Path.Combine(some.PluginFolder, "skills")).Select(Path.GetFileName));
        Assert.Contains("\"name\":\"openspace\"", File.ReadAllText(Path.Combine(some.PluginFolder, ".claude-plugin", "plugin.json")), StringComparison.Ordinal);
        Assert.Equal([some.PluginFolder, Skills], some.ReadFolders);
        Assert.Null(Catalog().For([]));
        Assert.Equal([Skills, Path.Combine(_root, "plugins")], Catalog().ReadFolders);
        Assert.Empty(Catalog(Path.Combine(_root, "missing")).ReadFolders);
    }

    [Fact]
    public void Rebuilding_a_plugin_never_touches_the_skills_it_points_at()
    {
        Catalog().For(["tdd"]);
        Catalog().For(["tdd"]);
        Catalog().For(null);
        Catalog().For(null);

        Assert.True(File.Exists(Path.Combine(Skills, "tdd", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(Skills, "security-audit", "SKILL.md")));
    }

    [Fact]
    public void A_restricted_Claude_Code_agent_loads_the_skills_reads_them_and_never_edits_them()
    {
        using var runtime = new ClaudeCodeRuntime(
            Options.Create(new ClaudeCodeOptions()),
            Options.Create(new OpenCodeOptions { ToolsUrl = new Uri("http://127.0.0.1:5145/mcp") }),
            new PermissionRequests(),
            new ClaudeCodeUsage(TimeProvider.System),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClaudeCodeRuntime>.Instance,
            skills: Catalog());
        var setup = new AgentSetup("build", null, null, RolePermissions.Default, Domain.Offices.AgentEngine.ClaudeCode,
            Skills: ["security-audit"], OutputFolder: "/srv/audits/e-corp/run-1");

        var args = runtime.SessionArguments(runtime.CreateSession("/srv/worktrees/a"), resume: false, setup, "/srv/worktrees/a");

        var plugin = args[args.IndexOf("--plugin-dir") + 1];
        Assert.Contains(ClaudeCodeRuntime.SkillTool, args.Skip(args.IndexOf("--tools") + 1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));
        Assert.Equal([plugin, Skills, "/srv/audits/e-corp/run-1"], args.Skip(args.IndexOf("--add-dir") + 1).Take(3));
        Assert.Contains($"Edit(/{Skills}/**)", args);
        Assert.Contains($"Write(/{plugin}/**)", args);
        Assert.DoesNotContain(args, a => a.Contains("/srv/audits", StringComparison.Ordinal) && a.StartsWith("Edit(", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_skills_a_Claude_Code_agent_gets_no_plugin_and_no_skill_tool()
    {
        Assert.Empty(ClaudeCodeRuntime.Folders(null, null));
        Assert.Equal(["--add-dir", "/srv/out"], ClaudeCodeRuntime.Folders(null, "/srv/out"));
    }

    [Fact]
    public void OpenCode_agents_load_only_their_role_skills_read_the_skills_folder_and_may_write_their_output()
    {
        var rules = OpenCodeRuntime.Rules(RolePermissions.Default, [], ["/home/me/.claude/skills", "/home/me/.local/share/openspace/skill-plugins"], ["security-audit"], "/srv/audits/run-1")
            .Select(r => System.Text.Json.JsonSerializer.Serialize(r))
            .ToList();
        var everything = OpenCodeRuntime.Rules(RolePermissions.Default, [], ["/home/me/.claude/skills"])
            .Select(r => System.Text.Json.JsonSerializer.Serialize(r));

        Assert.Contains("""{"permission":"skill","pattern":"*","action":"deny"}""", rules);
        Assert.Contains("""{"permission":"skill","pattern":"security-audit","action":"allow"}""", rules);
        Assert.Contains("""{"permission":"external_directory","pattern":"/home/me/.claude/skills/*","action":"allow"}""", rules);
        Assert.Contains("""{"permission":"edit","pattern":"/home/me/.claude/skills/*","action":"deny"}""", rules);
        Assert.Contains("""{"permission":"external_directory","pattern":"/home/me/.local/share/openspace/skill-plugins/*","action":"allow"}""", rules);
        Assert.Contains("""{"permission":"external_directory","pattern":"/srv/audits/run-1/*","action":"allow"}""", rules);
        Assert.DoesNotContain(everything, r => r.Contains("\"skill\"", StringComparison.Ordinal));
    }
}
