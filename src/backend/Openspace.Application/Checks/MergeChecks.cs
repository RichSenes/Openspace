using System.Globalization;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Checks;

public sealed class CheckOptions
{
    public const string SectionName = "Checks";

    /// <summary>How long one room's check may run before it is stopped.</summary>
    public int TimeoutMinutes { get; set; } = 20;
}

/// <param name="ExitCode">Null when the check was stopped for taking too long.</param>
public sealed record CheckOutcome(Guid RoomId, string Room, string Command, bool Passed, int? ExitCode, string Output, double Seconds);

/// <summary>
/// Runs the rooms' check commands on a branch, usually right after work was merged into it:
/// merges without conflicts can still break the code. The checks run in a throwaway copy of
/// the branch, so your checkout and the branch itself never move.
/// </summary>
public sealed class MergeChecks(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IWorkspaceManager workspaces,
    ICheckRunner runner,
    IUnitOfWork unitOfWork,
    IOptions<CheckOptions> options,
    TimeProvider clock)
{
    /// <param name="after">
    /// The tasks just merged: only their rooms are checked, and each gets a note with the
    /// result. Null checks every room that has a check.
    /// </param>
    public async Task<IReadOnlyList<CheckOutcome>> RunAsync(
        OfficeId officeId, string branch, IReadOnlyCollection<WorkTaskId>? after, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var merged = new List<WorkTask>();
        foreach (var id in after ?? [])
        {
            merged.Add(await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false));
        }

        var rooms = office.Rooms
            .Where(r => r.Check is not null)
            .Where(r => after is null || merged.Any(t => t.DeskId is { } desk && r.Desks.Any(d => d.Id == desk)))
            .ToList();
        if (rooms.Count == 0)
        {
            return [];
        }

        var timeout = TimeSpan.FromMinutes(Math.Max(1, options.Value.TimeoutMinutes));
        var folder = await workspaces.OpenScratchAsync(office.RepositoryPath!, branch, cancellationToken).ConfigureAwait(false);
        var outcomes = new List<CheckOutcome>();
        try
        {
            foreach (var room in rooms)
            {
                var run = await runner.RunAsync(folder, room.Check!, timeout, cancellationToken).ConfigureAwait(false);
                outcomes.Add(new CheckOutcome(room.Id.Value, room.Name, room.Check!, run.Passed, run.ExitCode, run.Output, Math.Round(run.Duration.TotalSeconds, 1)));
            }
        }
        finally
        {
            await workspaces.CloseScratchAsync(office.RepositoryPath!, folder, CancellationToken.None).ConfigureAwait(false);
        }

        if (merged.Count > 0)
        {
            var summary = $"Checks on {branch} after merging: {string.Join("; ", outcomes.Select(Describe))}.";
            merged.ForEach(t => t.AddNote(NoteAuthor.System, summary, clock.GetUtcNow()));
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return outcomes;
    }

    private static string Describe(CheckOutcome outcome) => outcome switch
    {
        { Passed: true } => $"{outcome.Room} passed",
        { ExitCode: null } => $"{outcome.Room} stopped after too long",
        _ => string.Create(CultureInfo.InvariantCulture, $"{outcome.Room} FAILED (exit {outcome.ExitCode})"),
    };
}
