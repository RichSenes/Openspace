using Microsoft.Extensions.Options;

namespace Openspace.Application.LocalModels;

/// <param name="Model">A local model as desks name it: <c>local/gemma</c>.</param>
/// <param name="JsonSchema">When given, the answer is JSON matching it.</param>
/// <param name="Waiting">Told, about once a minute, while the model waits for the graphics card, and once when it gets it.</param>
public sealed record LocalAsk(
    string Model, string System, string User, string? JsonSchema = null, int MaxTokens = 2048, double Temperature = 0.3, Action<string>? Waiting = null);

/// <summary>The model server's own API: one request, one answer.</summary>
public interface ILocalModelApi
{
    Task<string> CompleteAsync(LocalAsk ask, CancellationToken cancellationToken);
}

/// <summary>Asks a local model something, for routines.</summary>
public interface ILocalChat
{
    Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken);
}

/// <summary>
/// Asks a local model on behalf of a routine, through the arbiter like a task: it waits until
/// the model is loaded and nothing else uses it, holds it while it asks, and gives up after
/// <see cref="LocalModelOptions.RoutineWait"/> if programs that come first keep the card.
/// </summary>
public sealed class LocalChat(GpuArbiter gpu, ILocalModelApi api, IOptionsMonitor<LocalModelOptions> options, TimeProvider clock) : ILocalChat
{
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(3);

    public async Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ask);
        if (!gpu.IsLocal(ask.Model))
        {
            throw new InvalidOperationException($"{ask.Model} is not a local model, or local models are turned off.");
        }

        var giveUp = clock.GetUtcNow() + options.CurrentValue.RoutineWait;
        var told = DateTimeOffset.MinValue;
        IDisposable? lease;
        while ((lease = gpu.TryLease(ask.Model)) is null)
        {
            if (ask.Waiting is not null && clock.GetUtcNow() - told >= TimeSpan.FromMinutes(1))
            {
                var holders = gpu.View().Holders;
                ask.Waiting($"Waiting for the graphics card{(holders.Count > 0 ? $": {string.Join(", ", holders)} is using it" : string.Empty)}");
                told = clock.GetUtcNow();
            }

            if (clock.GetUtcNow() >= giveUp)
            {
                var view = gpu.View();
                throw new TimeoutException(
                    $"{ask.Model} was not free within {options.CurrentValue.RoutineWait.TotalMinutes:0} minutes ({view.State}{(view.Holders.Count > 0 ? $", the card is with {string.Join(", ", view.Holders)}" : "")}).");
            }

            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
        }

        if (told != DateTimeOffset.MinValue)
        {
            ask.Waiting?.Invoke("The graphics card is free: the local model is writing");
        }

        using (lease)
        {
            return await api.CompleteAsync(ask, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <param name="Type">"boolean", "choice" or "score".</param>
/// <param name="Options">For choices: the options, by id, with what each means.</param>
public sealed record DecisionQuestion(string Type, string Instructions, IReadOnlyList<DecisionOption>? Options = null);

public sealed record DecisionOption(string Id, string Description);

/// <param name="Answer">The most likely option ("true"/"false" for yes-no questions); null when it abstained.</param>
/// <param name="Confidence">How likely that option is, from 0 to 1.</param>
public sealed record Decision(string? Answer, double Confidence, bool Abstained);

/// <summary>
/// A small model on the processor that answers typed questions about a text: fast, cheap,
/// right most of the time. Routines use it to sort and filter, never alone where a mistake costs.
/// </summary>
public interface IDecider
{
    bool Available { get; }

    Task<IReadOnlyDictionary<string, Decision>> DecideAsync(
        string state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken cancellationToken);
}

public static class LocalChatExtensions
{
    /// <summary>Asks on behalf of a routine run: while the model waits for the graphics card, the run says so.</summary>
    public static Task<string> AskAsync(this ILocalChat chat, Routines.RoutineContext context, LocalAsk ask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ask);
        return chat.AskAsync(ask with { Waiting = context.Progress }, cancellationToken);
    }
}
