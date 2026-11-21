using Openspace.Domain.Roles;

namespace Openspace.Domain.Offices;

/// <summary>A seat in a room, bound to one OpenCode agent.</summary>
public sealed class Desk
{
    internal Desk(DeskId id, string name, AgentProfile agent)
    {
        Id = id;
        Name = name;
        Agent = agent;
        Personality = Personality.FromSeed(id.Value);
    }

    // Used by persistence, which sets Agent right after construction.
    private Desk(DeskId id, string name)
        : this(id, name, null!)
    {
    }

    public DeskId Id { get; }

    public string Name { get; private set; }

    public AgentProfile Agent { get; private set; }

    /// <summary>The role that tells the agent how to work; null for a plain OpenCode agent.</summary>
    public RoleId? RoleId { get; private set; }

    /// <summary>Where the desk stands in its room; null when the room places it automatically.</summary>
    public Spot? Spot { get; private set; }

    /// <summary>How its agent looks, chosen by hand; null to let Openspace pick from the desk's id.</summary>
    public DeskLook? Look { get; private set; }

    /// <summary>
    /// The traits as stored: fixed when the desk is created. Desks from before traits were
    /// stored without, and <see cref="Traits"/> gives them the ones of their id.
    /// </summary>
    public Personality Personality { get; private set; } = Personality.None;

    /// <summary>Two or three traits, fixed at creation; the same for the desk's whole life.</summary>
    public IReadOnlyList<Trait> Traits => Personality.IsEmpty ? Personality.FromSeed(Id.Value).Traits : Personality.Traits;

    /// <summary>The birthday the person chose; null when it is the one picked from the desk's id.</summary>
    public Birthday? ChosenBirthday { get; private set; }

    /// <summary>A day and a month: chosen by the person, or picked from the desk's id.</summary>
    public Birthday Birthday => ChosenBirthday ?? Birthday.FromSeed(Id.Value);

    internal void BeBorn(Personality personality) => Personality = personality;

    internal void CelebrateOn(Birthday? birthday) => ChosenBirthday = birthday;

    internal void MoveTo(Spot? spot) => Spot = spot;

    internal void Dress(DeskLook? look) => Look = look;

    internal void Reassign(AgentProfile agent) => Agent = agent;

    internal void GiveRole(RoleId? roleId) => RoleId = roleId;

    internal void Rename(string name) => Name = name;
}
