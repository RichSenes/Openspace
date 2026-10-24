namespace Openspace.Application.Work;

public enum MergeResult
{
    Merged,

    /// <summary>The work conflicts with the branch: its agent can bring it up to date.</summary>
    Conflict,

    Failed,

    /// <summary>Skipped because the branch could not take the work before it.</summary>
    NotTried,
}

/// <param name="Message">Why it was not merged, for anything but <see cref="MergeResult.Merged"/>.</param>
public sealed record MergeOutcome(Guid TaskId, string Title, MergeResult Result, string? Message);
