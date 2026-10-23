namespace Openspace.Domain.Work;

/// <summary>What the agent spent on one round of a task: the first, or one after the work was sent back.</summary>
public sealed class RoundUsage
{
    internal RoundUsage(int round) => Round = round;

    public int Round { get; }

    public long Input { get; private set; }

    public long Output { get; private set; }

    public long CacheRead { get; private set; }

    public long CacheWrite { get; private set; }

    public TokenCount Tokens => new(Input, Output, CacheRead, CacheWrite);

    internal void Add(TokenCount tokens)
    {
        Input += tokens.Input;
        Output += tokens.Output;
        CacheRead += tokens.CacheRead;
        CacheWrite += tokens.CacheWrite;
    }
}
