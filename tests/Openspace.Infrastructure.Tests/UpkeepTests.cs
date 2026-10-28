using System.Net;
using System.Text;
using System.Text.Json;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Upkeep;

namespace Openspace.Infrastructure.Tests;

/// <summary>Maintenance's readers on made-up projects and made-up answers of the public services.</summary>
public sealed class UpkeepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openspace-deps-{Guid.NewGuid():N}");

    public UpkeepTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void It_reads_npm_lock_files_with_the_direct_packages_told_apart()
    {
        Write("web/package-lock.json", """
            {"lockfileVersion":3,"packages":{
              "":{"name":"web","dependencies":{"tiny-router":"^2.1.0"},"devDependencies":{"@acme/lint":"1.0.0"}},
              "node_modules/tiny-router":{"version":"2.1.0"},
              "node_modules/@acme/lint":{"version":"1.0.0","dev":true},
              "node_modules/left-pad":{"version":"1.0.0"},
              "node_modules/tiny-router/node_modules/left-pad":{"version":"0.9.0"},
              "node_modules/local-thing":{"resolved":"../thing","link":true}
            }}
            """);
        Write("old/package.json", """{"dependencies":{"is-even":"1.0.0"}}""");
        Write("old/package-lock.json", """{"lockfileVersion":1,"dependencies":{"is-even":{"version":"1.0.0","dependencies":{"is-odd":{"version":"0.1.2"}}}}}""");

        var found = new DependencyFiles().Read(_root, []).OrderBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Version, StringComparer.Ordinal).ToList();

        Assert.Equal(
            [
                ("@acme/lint", "1.0.0", true), ("is-even", "1.0.0", true), ("is-odd", "0.1.2", false),
                ("left-pad", "0.9.0", false), ("left-pad", "1.0.0", false), ("tiny-router", "2.1.0", true),
            ],
            found.Select(d => (d.Name, d.Version, d.Direct)));
        Assert.All(found, d => Assert.Equal(Ecosystem.Npm, d.Ecosystem));
        Assert.Equal(Path.Combine("web", "package-lock.json"), found.Last().Manifest);
    }

    [Fact]
    public void It_reads_dotnet_projects_with_central_versions_and_what_the_restore_resolved()
    {
        Write("Directory.Packages.props", """
            <Project><ItemGroup><PackageVersion Include="Example.Json" Version="4.0.0" /><PackageVersion Include="Example.Http" Version="1.1.0" /></ItemGroup></Project>
            """);
        Write("src/Shop/Shop.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><ItemGroup>
              <PackageReference Include="Example.Json" />
              <PackageReference Include="Example.Http" VersionOverride="1.2.0" />
              <PackageReference Include="Example.Floating" Version="2.*" />
            </ItemGroup></Project>
            """);
        Write("src/Shop/obj/project.assets.json", """
            {"version":3,"libraries":{
              "Example.Json/4.0.0":{"type":"package"},
              "Example.Floating/2.3.0":{"type":"package"},
              "Example.Buffers/1.0.0":{"type":"package"},
              "Shop.Core/1.0.0":{"type":"project"}
            }}
            """);

        var found = new DependencyFiles().Read(_root, []).OrderBy(d => d.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(
            [("Example.Buffers", "1.0.0", false), ("Example.Floating", "2.3.0", true), ("Example.Http", "1.2.0", true), ("Example.Json", "4.0.0", true)],
            found.Select(d => (d.Name, d.Version, d.Direct)));
        Assert.All(found, d => Assert.Equal(Ecosystem.NuGet, d.Ecosystem));
    }

    [Fact]
    public void It_reads_pinned_python_requirements_only()
    {
        Write("requirements.txt", "markdownish==0.9.0\nhttp-thing[socks] == 2.31.0 ; python_version >= '3.8'\nloose>=1.0\n# comment==1.0\n");

        var found = new DependencyFiles().Read(_root, []);

        Assert.Equal([("markdownish", "0.9.0"), ("http-thing", "2.31.0")], found.Select(d => (d.Name, d.Version)));
        Assert.All(found, d => Assert.Equal(Ecosystem.PyPI, d.Ecosystem));
    }

    [Fact]
    public void It_never_enters_downloads_builds_hidden_or_private_folders()
    {
        var lockFile = """{"lockfileVersion":3,"packages":{"node_modules/left-pad":{"version":"1.0.0"}}}""";
        Write("node_modules/some/package-lock.json", lockFile);
        Write(".cache/package-lock.json", lockFile);
        Write("bin/package-lock.json", lockFile);
        Write("private/package-lock.json", lockFile);
        Write("app/package-lock.json", lockFile);

        var found = new DependencyFiles().Read(_root, [Path.Combine(_root, "private")]);

        Assert.Equal(Path.Combine("app", "package-lock.json"), Assert.Single(found).Manifest);
    }

    [Fact]
    public void An_advisory_gives_the_first_fix_after_the_version_in_use()
    {
        using var detail = JsonDocument.Parse("""
            {"id":"GHSA-aaaa","summary":"Padding overflows the buffer","aliases":["CVE-2015-0001"],
             "database_specific":{"severity":"HIGH"},
             "affected":[
               {"package":{"ecosystem":"npm","name":"left-pad"},"ranges":[{"type":"SEMVER","events":[{"introduced":"0"},{"fixed":"0.9.5"},{"introduced":"1.0.0"},{"fixed":"1.0.2"}]}]},
               {"package":{"ecosystem":"npm","name":"other-pad"},"ranges":[{"type":"SEMVER","events":[{"introduced":"0"},{"fixed":"9.0.0"}]}]}
             ]}
            """);

        var advisory = OsvAdvisories.Read(detail.RootElement, new Dependency(Ecosystem.Npm, "left-pad", "1.0.0", true, "package-lock.json"));
        var older = OsvAdvisories.Read(detail.RootElement, new Dependency(Ecosystem.Npm, "left-pad", "0.9.0", true, "package-lock.json"));

        Assert.Equal(("CVE-2015-0001", Severity.High, "1.0.2"), (advisory.Name, advisory.Severity, advisory.Fixed));
        Assert.Equal("Padding overflows the buffer", advisory.Summary);
        Assert.Equal("0.9.5", older.Fixed);
    }

    [Fact]
    public async Task It_asks_osv_in_one_batch_and_reads_each_advisory()
    {
        var osv = new FakeOsv();
        var advisories = new OsvAdvisories(new HttpClient(osv));
        var pad = new Dependency(Ecosystem.Npm, "left-pad", "1.0.0", true, "package-lock.json");
        var json = new Dependency(Ecosystem.NuGet, "Example.Json", "4.0.0", false, "Shop.csproj");

        var found = await advisories.FindAsync([pad, json], default);

        var only = Assert.Single(found);
        Assert.Equal(IAdvisories.Key(pad), only.Key);
        Assert.Equal(("GHSA-aaaa", Severity.Critical, (string?)"1.0.2"), (only.Value[0].Id, only.Value[0].Severity, only.Value[0].Fixed));
        Assert.Contains("\"ecosystem\":\"NuGet\"", osv.Batches.Single(), StringComparison.Ordinal);
        Assert.Equal(["/v1/querybatch", "/v1/vulns/GHSA-aaaa"], osv.Paths);
    }

    [Theory]
    [InlineData(Ecosystem.Npm, """{"name":"left-pad","version":"1.3.0"}""", "1.3.0")]
    [InlineData(Ecosystem.NuGet, """{"versions":["1.0.0","1.10.0","1.9.0","2.0.0-rc.1"]}""", "1.10.0")]
    [InlineData(Ecosystem.PyPI, """{"info":{"version":"0.9.1"}}""", "0.9.1")]
    public void The_registries_answers_give_the_latest_stable(Ecosystem ecosystem, string answer, string latest)
    {
        using var json = JsonDocument.Parse(answer);
        Assert.Equal(latest, PackageRegistries.Latest(ecosystem, json.RootElement));
    }

    [Fact]
    public void Dotnet_lines_are_checked_against_the_release_index()
    {
        using var index = JsonDocument.Parse("""
            {"releases-index":[
              {"channel-version":"10.0","latest-sdk":"10.0.403","latest-runtime":"10.0.14","security":true,"eol-date":"2028-11-14"},
              {"channel-version":"9.0","latest-sdk":"9.0.320","latest-runtime":"9.0.22","security":false,"eol-date":"2026-11-10"}
            ]}
            """);
        var sdks = Toolchains.Installed("10.0.303 [/home/user/.dotnet/sdk]\n10.0.402 [/home/user/.dotnet/sdk]\n9.0.320 [/home/user/.dotnet/sdk]\n", null);
        var runtimes = Toolchains.Installed("Microsoft.AspNetCore.App 10.0.13 [/x]\nMicrosoft.NETCore.App 10.0.13 [/x]\nMicrosoft.NETCore.App 10.0.11 [/x]\n", "Microsoft.NETCore.App");

        var found = Toolchains.DotNet(sdks, runtimes, index.RootElement).ToList();

        Assert.Equal(
            [
                new Toolchain(".NET SDK", "10.0.402", "10.0.403", true, new DateOnly(2028, 11, 14)),
                new Toolchain(".NET SDK", "9.0.320", null, false, new DateOnly(2026, 11, 10)),
                new Toolchain(".NET runtime", "10.0.13", "10.0.14", true, new DateOnly(2028, 11, 14)),
            ],
            found);
    }

    [Fact]
    public void Node_counts_a_security_release_after_the_installed_one()
    {
        using var releases = JsonDocument.Parse("""
            [{"version":"v24.1.0","security":true},{"version":"v22.24.0","security":false},{"version":"v22.23.5","security":true},{"version":"v22.23.4","security":false}]
            """);
        using var schedule = JsonDocument.Parse("""{"v22":{"start":"2024-04-24","end":"2027-04-30"}}""");

        Assert.Equal(new Toolchain("Node", "22.23.4", "22.24.0", true, new DateOnly(2027, 4, 30)), Toolchains.Node("v22.23.4\n", releases.RootElement, schedule.RootElement));
        Assert.Equal(new Toolchain("Node", "22.24.0", null, false, new DateOnly(2027, 4, 30)), Toolchains.Node("v22.24.0", releases.RootElement, schedule.RootElement));
    }

    [Fact]
    public void System_tools_outputs_are_read()
    {
        Assert.Equal((104, 3), SystemPackages.AptCounts("104;3"));
        Assert.Equal((0, 0), SystemPackages.AptCounts("garbled"));
        Assert.Equal(["firefox", "code"], SystemPackages.Snaps("Name     Version  Rev   Size   Publisher  Notes\nfirefox  159.0-1  9137  297MB  mozilla**  -\ncode     1.0      1     10MB   vscode**   classic\n"));
        Assert.Empty(SystemPackages.Snaps("All snaps up to date.\n"));
        Assert.Equal(["org.example.Editor"], SystemPackages.Flatpaks("Application ID\norg.example.Editor\n"));
    }

    [Fact]
    public async Task A_repository_without_a_remote_counts_its_work_as_only_here()
    {
        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repository);
        async Task Git(params string[] arguments) => Assert.Equal(0, (await Commands.RunAsync("git", ["-C", repository, .. arguments], default))!.ExitCode);
        await Git("init", "-q", "-b", "main");
        await Git("-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-q", "--allow-empty", "-m", "First");
        File.WriteAllText(Path.Combine(repository, "notes.txt"), "draft");

        var state = await new RepositoryStates().ReadAsync(repository, default);

        Assert.Equal(new RepositoryState("main", 1, 1, false), state);
        Assert.Null(await new RepositoryStates().ReadAsync(_root, default));
    }

    private sealed class FakeOsv : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public List<string> Batches { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            string json;
            if (request.Method == HttpMethod.Post)
            {
                Batches.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                json = """{"results":[{"vulns":[{"id":"GHSA-aaaa","modified":"2015-06-01T00:00:00Z"}]},{}]}""";
            }
            else
            {
                json = """
                    {"id":"GHSA-aaaa","summary":"Padding overflows the buffer","database_specific":{"severity":"CRITICAL"},
                     "affected":[{"package":{"ecosystem":"npm","name":"left-pad"},"ranges":[{"type":"SEMVER","events":[{"introduced":"0"},{"fixed":"1.0.2"}]}]}]}
                    """;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
