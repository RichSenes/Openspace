namespace Openspace.Domain.Work;

/// <summary>Tokens a model read and wrote; cache reads and writes are counted apart, as engines report them.</summary>
public sealed record TokenCount(long Input, long Output, long CacheRead, long CacheWrite)
{
    public static readonly TokenCount Zero = new(0, 0, 0, 0);

    public long Total => Input + Output + CacheRead + CacheWrite;

    public static TokenCount operator +(TokenCount left, TokenCount right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new(left.Input + right.Input, left.Output + right.Output, left.CacheRead + right.CacheRead, left.CacheWrite + right.CacheWrite);
    }

    public static TokenCount Add(TokenCount left, TokenCount right) => left + right;
}
