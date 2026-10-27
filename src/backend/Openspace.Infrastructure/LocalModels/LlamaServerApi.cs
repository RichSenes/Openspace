using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Openspace.Application.LocalModels;

namespace Openspace.Infrastructure.LocalModels;

/// <summary>The OpenAI-compatible chat API of the local model server (llama.cpp's llama-server).</summary>
internal sealed class LlamaServerApi(HttpClient http, IOptionsMonitor<LocalModelOptions> options) : ILocalModelApi
{
    public async Task<string> CompleteAsync(LocalAsk ask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ask);
        var settings = options.CurrentValue;
        var body = new JsonObject
        {
            ["model"] = ask.Model[(ask.Model.IndexOf('/', StringComparison.Ordinal) + 1)..],
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = ask.System },
                new JsonObject { ["role"] = "user", ["content"] = ask.User }),
            ["max_tokens"] = ask.MaxTokens,
            ["temperature"] = ask.Temperature,
        };
        if (ask.JsonSchema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "answer", ["schema"] = JsonNode.Parse(ask.JsonSchema) },
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.ServerUrl, "/v1/chat/completions"))
        {
            Content = JsonContent.Create(body),
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKeyFile) && File.Exists(settings.ApiKeyFile))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", (await File.ReadAllTextAsync(settings.ApiKeyFile, cancellationToken).ConfigureAwait(false)).Trim());
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The local model answered {(int)response.StatusCode}: {Shorten(text)}");
        }

        using var document = JsonDocument.Parse(text);
        var choice = document.RootElement.GetProperty("choices")[0];
        var content = choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        if (choice.TryGetProperty("finish_reason", out var reason) && reason.GetString() == "length" && ask.JsonSchema is not null)
        {
            throw new InvalidOperationException($"The local model ran out of room ({ask.MaxTokens} tokens) before finishing its answer.");
        }

        return content.Trim();
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : string.Concat(text.AsSpan(0, 300), "…");
}
