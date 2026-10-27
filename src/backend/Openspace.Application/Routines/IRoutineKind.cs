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
    Task SendAsync(Routine routine, string text, string? filePath, CancellationToken cancellationToken);
}

internal sealed class NoOutbox : IRoutineOutbox
{
    public Task SendAsync(Routine routine, string text, string? filePath, CancellationToken cancellationToken) => Task.CompletedTask;
}
