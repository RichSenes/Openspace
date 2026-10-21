namespace Openspace.Domain.Work;

/// <summary>One required step of a task's procedure, and the agent's evidence once it is done.</summary>
public sealed class ProcedureStep
{
    internal ProcedureStep(int number, string text)
    {
        Number = number;
        Text = text;
    }

    /// <summary>1-based, as the agent sees it.</summary>
    public int Number { get; }

    public string Text { get; }

    /// <summary>What the agent did to satisfy the step: paths, commands, results.</summary>
    public string? Evidence { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsDone => CompletedAt is not null;

    internal void Complete(string evidence, DateTimeOffset now)
    {
        Evidence = evidence;
        CompletedAt = now;
    }
}
