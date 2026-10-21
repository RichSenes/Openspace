namespace Openspace.Domain.Work;

public enum WorkTaskStatus
{
    /// <summary>Waiting for a human to approve it (e.g. proposed by a supervisor).</summary>
    Proposed,

    /// <summary>Approved and waiting for a free seat.</summary>
    Queued,

    /// <summary>An agent is working on it.</summary>
    Running,

    /// <summary>The agent finished; a human must accept or send it back.</summary>
    Review,

    /// <summary>The agent could not finish it.</summary>
    Failed,

    /// <summary>Accepted. Terminal.</summary>
    Done,

    /// <summary>Dropped. Terminal.</summary>
    Discarded,
}
