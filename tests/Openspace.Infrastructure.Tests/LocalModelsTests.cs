using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Infrastructure.LocalModels;

namespace Openspace.Infrastructure.Tests;

public class LocalModelsTests
{
    private static LocalModelOptions Options() => new()
    {
        Enabled = true,
        ServerUrl = new Uri("http://127.0.0.1:9"),
        ApiKeyFile = "/opt/models/.key",
        Models =
        [
            new LocalModel { Id = "gemma", Name = "Gemma", Command = "/opt/models/start-gemma.sh", Context = 32768 },
            new LocalModel { Id = "tiny", Command = "/opt/models/start-tiny.sh", Context = 8192 },
        ],
    };

    [Fact]
    public void The_driver_tool_s_memory_line_is_read_in_mebibytes()
    {
        Assert.Equal(new GpuMemory(464, 12288), GpuProbe.ParseMemory("464, 12288\n"));
        Assert.Equal(new GpuMemory(100, 8192), GpuProbe.ParseMemory("100, 8192\n200, 24576\n"));
        Assert.Null(GpuProbe.ParseMemory("No devices were found"));
    }

    [Fact]
    public void Holders_are_found_in_command_lines_whatever_the_case()
    {
        GpuHolder[] holders =
        [
            new() { Name = "Image studio", Match = "image-studio" },
            new() { Name = "Trainer", Match = "trainer/train.py" },
            new() { Name = "Empty", Match = " " },
        ];

        var running = GpuProbe.Match(holders, ["/usr/bin/bash", "dotnet /apps/Image-Studio/ImageStudio.dll", "python3 scripts/other.py"]);

        Assert.Equal(["Image studio"], running);
    }

    [Fact]
    public void The_opencode_provider_names_every_model_and_points_to_the_key_file()
    {
        using var json = JsonDocument.Parse(OpenCodeLocalProvider.Render(Options()));
        var provider = json.RootElement.GetProperty("provider").GetProperty("local");

        Assert.Equal("http://127.0.0.1:9/v1", provider.GetProperty("options").GetProperty("baseURL").GetString());
        Assert.Equal("{file:/opt/models/.key}", provider.GetProperty("options").GetProperty("apiKey").GetString());
        Assert.Equal("Gemma", provider.GetProperty("models").GetProperty("gemma").GetProperty("name").GetString());
        Assert.Equal("tiny", provider.GetProperty("models").GetProperty("tiny").GetProperty("name").GetString());
        Assert.Equal(2048, provider.GetProperty("models").GetProperty("tiny").GetProperty("limit").GetProperty("output").GetInt32());
    }

    [Fact]
    public async Task The_provider_file_is_written_only_when_it_changes()
    {
        var options = Options();
        options.OpenCodeConfigPath = Path.Combine(Path.GetTempPath(), $"openspace-local-{Guid.NewGuid():N}", "opencode.json");
        try
        {
            Assert.True(await OpenCodeLocalProvider.WriteAsync(options, default));
            Assert.False(await OpenCodeLocalProvider.WriteAsync(options, default));

            options.Models.RemoveAt(1);
            Assert.True(await OpenCodeLocalProvider.WriteAsync(options, default));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(options.OpenCodeConfigPath)!, recursive: true);
        }
    }

    [Fact]
    public async Task The_server_process_is_followed_and_stopped()
    {
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        using var server = new LocalModelServer(
            services.GetRequiredService<IHttpClientFactory>(), () => Options().ServerUrl, NullLogger<LocalModelServer>.Instance);
        var model = new LocalModel { Id = "sleepy", Command = "/bin/sh", Arguments = ["-c", "sleep 60"] };

        await server.StartAsync(model, default);
        Assert.Equal("sleepy", server.Running);
        Assert.False(await server.IsAnsweringAsync(default));

        await server.StopAsync(default);
        Assert.Null(server.Running);
    }

    [Fact]
    public async Task A_server_that_stops_by_itself_is_no_longer_running()
    {
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        using var server = new LocalModelServer(
            services.GetRequiredService<IHttpClientFactory>(), () => Options().ServerUrl, NullLogger<LocalModelServer>.Instance);

        await server.StartAsync(new LocalModel { Id = "broken", Command = "/bin/sh", Arguments = ["-c", "echo 'syntax error' >&2; exit 2"] }, default);
        for (var i = 0; i < 50 && server.Running is not null; i++)
        {
            await Task.Delay(100);
        }

        Assert.Null(server.Running);
    }
}
