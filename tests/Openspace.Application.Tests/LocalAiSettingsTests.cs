using Openspace.Application.LocalModels;
using Openspace.Domain.Common;

namespace Openspace.Application.Tests;

public class LocalAiSettingsTests
{
    private static LocalModelView Model(string id = "gemma", string command = "/opt/llm/gemma.sh", int memory = 9000, params string[] arguments) =>
        new(id, "Gemma", command, arguments, memory, 32768);

    private static LocalAiSettingsView Settings(params LocalModelView[] models) =>
        new(true, "http://127.0.0.1:8080", models, [new GpuHolderView(" Image studio ", " studio.py ")], 10, 3, new DeciderView("", [], "http://127.0.0.1:8017"));

    [Fact]
    public void Settings_are_trimmed_and_blank_arguments_dropped()
    {
        var valid = LocalAiSettings.Validate(Settings(Model(" gemma-4 ", arguments: ["-m", " ", "/m/gemma.gguf "])));

        var model = Assert.Single(valid.Models);
        Assert.Equal("gemma-4", model.Id);
        Assert.Equal(["-m", "/m/gemma.gguf"], model.Arguments);
        Assert.Equal(new GpuHolderView("Image studio", "studio.py"), Assert.Single(valid.Holders));
    }

    [Theory]
    [InlineData("Gemma 4", "/opt/llm/g.sh", 9000)]
    [InlineData("gemma", "", 9000)]
    [InlineData("gemma", "/opt/llm/g.sh", -1)]
    public void A_model_a_server_could_not_start_is_refused(string id, string command, int memory) =>
        Assert.Throws<DomainException>(() => LocalAiSettings.Validate(Settings(Model(id, command, memory))));

    [Fact]
    public void Two_models_with_one_name_and_bad_addresses_are_refused()
    {
        Assert.Throws<DomainException>(() => LocalAiSettings.Validate(Settings(Model(), Model())));
        Assert.Throws<DomainException>(() => LocalAiSettings.Validate(Settings() with { ServerUrl = "localhost" }));
        Assert.Throws<DomainException>(() => LocalAiSettings.Validate(Settings() with { IdleUnloadMinutes = 0 }));
    }

    [Fact]
    public void A_check_says_what_is_there_and_what_keeps_the_model_from_loading()
    {
        var model = Model(command: "llama-server", memory: 12000, arguments: ["-m", "/m/gemma.gguf", "/m/missing.gguf", "--port"]);

        var check = LocalAiSettings.Check(model, path => path == "/m/gemma.gguf", command => command == "llama-server", cardMiB: 8192);

        Assert.False(check.Ok);
        Assert.Contains("llama-server is there.", check.Fine);
        Assert.Contains("gemma.gguf is there.", check.Fine);
        Assert.Contains("/m/missing.gguf does not exist.", check.Problems);
        Assert.Contains("It needs 12000 MiB and the card has 8192 MiB.", check.Problems);
    }

    [Fact]
    public void A_model_that_fits_and_finds_its_files_passes()
    {
        var check = LocalAiSettings.Check(Model(), _ => true, _ => false, cardMiB: null);

        Assert.True(check.Ok);
        Assert.Equal(["/opt/llm/gemma.sh is there."], check.Fine);
    }

    [Fact]
    public void Blank_items_left_by_a_shorter_list_are_dropped()
    {
        var options = new LocalModelOptions
        {
            Models = [new LocalModel { Id = "gemma", Arguments = ["-m", ""] }, new LocalModel { Id = "" }],
            Holders = [new GpuHolder { Name = "", Match = "" }],
        };

        LocalAiSettings.DropBlanks(options);

        Assert.Equal(["-m"], Assert.Single(options.Models).Arguments);
        Assert.Empty(options.Holders);
    }
}
