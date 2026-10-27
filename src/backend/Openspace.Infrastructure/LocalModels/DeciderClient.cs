using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.LocalModels;

namespace Openspace.Infrastructure.LocalModels;

/// <summary>
/// Rizzo-Flow's typed decisions API on the processor: started when a routine first asks,
/// stopped by the graphics card shift once nobody asked for a while.
/// </summary>
internal sealed class DeciderClient(
    IHttpClientFactory httpClients, IOptionsMonitor<LocalModelOptions> options, TimeProvider clock, ILogger<LocalModelServer> logger)
    : IDecider, IDisposable
{
    public const string HttpClientName = "decider";

    private readonly LocalModelServer _server = new(httpClients, () => options.CurrentValue.Decider.Url, logger);
    private readonly SemaphoreSlim _starting = new(1, 1);
    private DateTimeOffset _lastUsed;

    public bool Available => !string.IsNullOrWhiteSpace(options.CurrentValue.Decider.Command);

    internal string? Running => _server.Running;

    public async Task<IReadOnlyDictionary<string, Decision>> DecideAsync(
        string state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questions);

        // In use from now: the idle check must not stop it while it starts.
        _lastUsed = clock.GetUtcNow();
        await EnsureRunningAsync(cancellationToken).ConfigureAwait(false);

        var body = new JsonObject { ["state"] = state, ["questions"] = new JsonObject() };
        foreach (var (key, question) in questions)
        {
            var node = new JsonObject { ["type"] = question.Type, ["instructions"] = question.Instructions };
            if (question.Options is { Count: > 0 } choices)
            {
                node["options"] = new JsonArray([.. choices.Select(o => (JsonNode)new JsonObject { ["id"] = o.Id, ["description"] = o.Description })]);
            }

            body["questions"]![key] = node;
        }

        using var http = httpClients.CreateClient(HttpClientName);
        using var response = await http.PostAsJsonAsync(new Uri(options.CurrentValue.Decider.Url, "/v1/decisions"), body, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The decider answered {(int)response.StatusCode}: {(text.Length > 300 ? text[..300] : text)}");
        }

        _lastUsed = clock.GetUtcNow();
        return Read(text);
    }

    /// <summary>Each answer: its most likely option, how likely, and whether it abstained.</summary>
    internal static IReadOnlyDictionary<string, Decision> Read(string text)
    {
        using var document = JsonDocument.Parse(text);
        var result = new Dictionary<string, Decision>(StringComparer.Ordinal);
        foreach (var answer in document.RootElement.GetProperty("answers").EnumerateObject())
        {
            var abstained = answer.Value.TryGetProperty("status", out var status) && status.GetString() != "ok";
            var best = answer.Value.TryGetProperty("probabilities", out var probabilities)
                ? probabilities.EnumerateObject().Where(p => !p.Name.StartsWith("__", StringComparison.Ordinal))
                    .Select(p => (p.Name, Value: p.Value.GetDouble())).OrderByDescending(p => p.Value).FirstOrDefault()
                : default;
            result[answer.Name] = new Decision(best.Name, best.Value, abstained || best.Name is null);
        }

        return result;
    }

    /// <summary>Stops the decider once nobody asked it for a while; called every few seconds.</summary>
    public async Task StopIfIdleAsync(CancellationToken cancellationToken)
    {
        if (_server.Running is not null && clock.GetUtcNow() - _lastUsed >= options.CurrentValue.Decider.IdleUnload)
        {
            await _server.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => _server.Running is null ? Task.CompletedTask : _server.StopAsync(cancellationToken);

    public void Dispose()
    {
        _server.Dispose();
        _starting.Dispose();
    }

    private async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue.Decider;
        if (string.IsNullOrWhiteSpace(settings.Command))
        {
            throw new InvalidOperationException("No decider is set up: LocalModels:Decider:Command is empty.");
        }

        await _starting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _server.IsAnsweringAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (_server.Running is null)
            {
                await _server.StartAsync(new LocalModel { Id = "decider", Command = settings.Command, Arguments = settings.Arguments }, cancellationToken).ConfigureAwait(false);
            }

            var giveUp = clock.GetUtcNow() + settings.StartTimeout;
            while (!await _server.IsAnsweringAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_server.Running is null || clock.GetUtcNow() >= giveUp)
                {
                    await StopAsync(cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException("The decider did not start: see the API log for its last lines.");
                }

                    await Task.Delay(TimeSpan.FromSeconds(1), clock, cancellationToken).ConfigureAwait(false);
                _lastUsed = clock.GetUtcNow();
            }
        }
        finally
        {
            _starting.Release();
        }
    }
}
