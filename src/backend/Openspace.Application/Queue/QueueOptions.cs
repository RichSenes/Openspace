namespace Openspace.Application.Queue;

public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    /// <summary>How many agents may work at the same time.</summary>
    public int Seats { get; set; } = 1;

    /// <summary>Run the queue in the background. Off in tests.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How often the files changed by running tasks are read again for Now.md.</summary>
    public TimeSpan NowInterval { get; set; } = TimeSpan.FromSeconds(5);
}
