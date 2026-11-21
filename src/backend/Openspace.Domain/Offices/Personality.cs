using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>A side of an inhabitant's character. It colours how they talk, never what they deliver.</summary>
public enum Trait
{
    Calm,
    Chatty,
    Meticulous,
    Bold,
    Shy,
    Sunny,
    DryHumoured,
    Curious,
}

/// <summary>
/// The traits an inhabitant is born with: two or three, fixed at creation and kept apart from
/// the role, so that a reviewer may be chatty and a writer shy.
/// </summary>
public sealed record Personality
{
    public const int MinTraits = 2;
    public const int MaxTraits = 3;

    /// <summary>Traits that cannot live in one character.</summary>
    private static readonly (Trait, Trait)[] Opposites = [(Trait.Chatty, Trait.Shy), (Trait.Calm, Trait.Bold)];

    private Personality(IReadOnlyList<Trait> traits) => Traits = traits;

    /// <summary>Its traits, in the order they were given; empty only for desks from before traits.</summary>
    public IReadOnlyList<Trait> Traits { get; }

    public bool IsEmpty => Traits.Count == 0;

    /// <summary>What desks from before traits were stored with: their traits then come from their id.</summary>
    public static readonly Personality None = new([]);

    public static Personality Of(IEnumerable<Trait> traits)
    {
        ArgumentNullException.ThrowIfNull(traits);
        var list = traits.Distinct().ToList();
        if (list.Exists(t => !Enum.IsDefined(t)))
        {
            throw new DomainException("Unknown trait.");
        }

        if (list.Count is < MinTraits or > MaxTraits)
        {
            throw new DomainException($"A character has {MinTraits} or {MaxTraits} traits.");
        }

        if (Opposites.FirstOrDefault(o => list.Contains(o.Item1) && list.Contains(o.Item2)) is { Item1: var a, Item2: var b } && a != b)
        {
            throw new DomainException($"{a} and {b} do not go together in one character.");
        }

        return new Personality(list);
    }

    /// <summary>
    /// Traits picked from an id: the same id always gives the same ones. New desks are born this
    /// way unless the person chooses, and desks from before traits keep the ones of their id.
    /// </summary>
    public static Personality FromSeed(Guid seed)
    {
        var hash = Seed.Of(seed);
        var count = MinTraits + (int)(hash % (MaxTraits - MinTraits + 1));
        var all = Enum.GetValues<Trait>();
        var picked = new List<Trait>();
        var step = 0UL;
        while (picked.Count < count)
        {
            var candidate = all[(int)(Seed.Mix(hash + step++) % (ulong)all.Length)];
            if (!picked.Contains(candidate) && !Opposites.Any(o => (o.Item1 == candidate && picked.Contains(o.Item2)) || (o.Item2 == candidate && picked.Contains(o.Item1))))
            {
                picked.Add(candidate);
            }
        }

        return new Personality(picked);
    }

    /// <summary>"Calm,Chatty": how persistence keeps it.</summary>
    public override string ToString() => string.Join(',', Traits);

    public static Personality Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        var traits = new List<Trait>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<Trait>(part, ignoreCase: true, out var trait) && Enum.IsDefined(trait) && !traits.Contains(trait))
            {
                traits.Add(trait);
            }
        }

        return traits.Count == 0 ? None : new Personality(traits);
    }

    public bool Equals(Personality? other) => other is not null && Traits.SequenceEqual(other.Traits);

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);
}

/// <summary>A stable number from an id, the same on every machine and every run.</summary>
internal static class Seed
{
    public static ulong Of(Guid id)
    {
        // FNV-1a over the id's bytes: string.GetHashCode changes between runs.
        var hash = 14695981039346656037UL;
        foreach (var b in id.ToByteArray())
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        return Mix(hash);
    }

    /// <summary>SplitMix64's finaliser: spreads close numbers far apart.</summary>
    public static ulong Mix(ulong x)
    {
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}
