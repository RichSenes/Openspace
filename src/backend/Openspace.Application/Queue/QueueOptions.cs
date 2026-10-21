namespace Openspace.Application.Queue;

public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    /// <summary>How many agents may work at the same time.</summary>
    public int Seats { get; set; } = 1;
}
