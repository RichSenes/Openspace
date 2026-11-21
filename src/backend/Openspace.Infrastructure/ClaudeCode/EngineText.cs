using Openspace.Application.Functions;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>Text from Claude Code or OpenCode on the model a function names, with no tools and nothing kept.</summary>
internal sealed class EngineText(ClaudeCodeRuntime claude, OpenCodeRuntime openCode) : IEngineText
{
    public Task<string> AskAsync(ModelEngine engine, string? model, string instructions, string text, CancellationToken cancellationToken) =>
        engine switch
        {
            ModelEngine.ClaudeCode => claude.AskAsync(Path.GetTempPath(), instructions, text, null, [], [], ClaudeCodeOptions.ModelOrDefault(model), cancellationToken),
            ModelEngine.OpenCode => openCode.DraftAsync(instructions, text, ClaudeCodeOptions.ModelOrDefault(model), cancellationToken),
            _ => throw new InvalidOperationException($"{engine} models are asked through the local chat."),
        };
}
