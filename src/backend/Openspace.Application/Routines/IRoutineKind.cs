using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <summary>
/// What a routine does: the morning paper, the job offers of the day. A kind runs with a
/// <see cref="RoutineContext"/>, which gives it its settings, a log, the memory of what it
/// already met and a way to propose things; it never acts on the person's things itself
/// until a proposal of its own is approved.
/// </summary>
public interface IRoutineKind
{
    /// <summary>How routines name it, e.g. "morning-paper". Never changes once used.</summary>
    string Key { get; }

    string Name { get; }

    /// <summary>A sentence for people: what a routine of this kind does.</summary>
    string Description { get; }

    /// <summary>The settings a new routine of this kind starts with, as a JSON object.</summary>
    string DefaultSettings => "{}";

    /// <returns>A line for people: what came out of this run.</returns>
    Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Whether doing it can be undone (a label, a reminder, a file saved in its own folder):
    /// then a routine that does not ask first does it on its own. Sending, deleting or
    /// applying never are.
    /// </summary>
    bool CanUndo(string action) => false;

    /// <summary>A proposal of this kind was approved: carry it out.</summary>
    /// <returns>What was done, for people.</returns>
    Task<string> CarryOutAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"Routines of kind {Key} make no proposals.");

    /// <summary>A proposal of this kind was turned down: remember it, if it matters.</summary>
    Task RejectedAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Where routines send what they make: a chat, for now nowhere but the run's log.</summary>
public interface IRoutineOutbox
{
    /// <param name="filePath">A file to attach, such as the paper's PDF.</param>
    /// <returns>False when the routine names no channel, so nothing was sent.</returns>
    Task<bool> SendAsync(Routine routine, string text, string? filePath, CancellationToken cancellationToken);

    /// <summary>A message of its own, formatted: one offer, one reminder.</summary>
    /// <returns>Its number in the chat, to answer it later; null when the routine names no channel.</returns>
    Task<long?> PostAsync(Routine routine, OutboxMessage message, CancellationToken cancellationToken);

    /// <summary>The channels a routine may name, by name only: never their keys.</summary>
    IReadOnlyList<string> Channels() => [];
}

/// <summary>A message in Telegram's HTML (bold, italics, links), with its text plain for where HTML is not read.</summary>
public sealed record OutboxMessage(string Html, string Plain)
{
    /// <summary>Text made safe inside the HTML of a message.</summary>
    public static string Escape(string text) =>
        (text ?? string.Empty).Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}

internal sealed class NoOutbox : IRoutineOutbox
{
    public Task<bool> SendAsync(Routine routine, string text, string? filePath, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<long?> PostAsync(Routine routine, OutboxMessage message, CancellationToken cancellationToken) => Task.FromResult<long?>(null);
}

/// <summary>What a running routine is doing, line by line, for whoever watches: the building's bubbles.</summary>
public interface IRoutineProgress
{
    void Report(Routine routine, string line);
}

internal sealed class NoProgress : IRoutineProgress
{
    public void Report(Routine routine, string line)
    {
    }
}
