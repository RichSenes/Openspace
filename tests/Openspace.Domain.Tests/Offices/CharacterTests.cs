using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Offices;

public class CharacterTests
{
    [Fact]
    public void A_new_desk_is_born_with_two_or_three_traits_that_go_together()
    {
        for (var i = 0; i < 200; i++)
        {
            var traits = Personality.FromSeed(Guid.NewGuid()).Traits;
            Assert.InRange(traits.Count, Personality.MinTraits, Personality.MaxTraits);
            Assert.Equal(traits.Count, traits.Distinct().Count());
            Assert.False(traits.Contains(Trait.Chatty) && traits.Contains(Trait.Shy));
            Assert.False(traits.Contains(Trait.Calm) && traits.Contains(Trait.Bold));
        }
    }

    [Fact]
    public void The_same_id_always_gives_the_same_traits_and_birthday()
    {
        var id = Guid.Parse("7f3c2a10-0000-4000-8000-000000000042");
        Assert.Equal(Personality.FromSeed(id), Personality.FromSeed(id));
        Assert.Equal(Birthday.FromSeed(id), Birthday.FromSeed(id));
    }

    [Fact]
    public void A_desk_stores_its_traits_at_creation()
    {
        var (_, desk) = ECorpWithDesk();
        Assert.False(desk.Personality.IsEmpty);
        Assert.Equal(desk.Personality.Traits, desk.Traits);
    }

    [Fact]
    public void Traits_can_be_chosen_at_creation_and_are_never_bundled_with_the_role()
    {
        var office = ECorp();
        var room = office.OpenRoom("Web", Now);
        var desk = office.AddDesk(room.Id, "Frontend", AgentProfile.Create("frontend-dev"), Now, personality: Personality.Of([Trait.Shy, Trait.Meticulous]));

        Assert.Equal([Trait.Shy, Trait.Meticulous], desk.Traits);
        Assert.Null(desk.RoleId);
    }

    [Theory]
    [InlineData(new[] { Trait.Calm })]
    [InlineData(new[] { Trait.Calm, Trait.Shy, Trait.Sunny, Trait.Curious })]
    [InlineData(new[] { Trait.Chatty, Trait.Shy })]
    [InlineData(new[] { Trait.Calm, Trait.Bold })]
    public void A_character_has_two_or_three_traits_that_go_together(Trait[] traits)
    {
        Assert.Throws<DomainException>(() => Personality.Of(traits));
    }

    [Fact]
    public void Traits_survive_their_stored_text()
    {
        var personality = Personality.Of([Trait.DryHumoured, Trait.Curious, Trait.Calm]);
        Assert.Equal(personality, Personality.Parse(personality.ToString()));
        Assert.True(Personality.Parse("").IsEmpty);
        Assert.True(Personality.Parse("nonsense").IsEmpty);
    }

    [Fact]
    public void A_birthday_has_no_year_and_29_february_is_kept_on_the_28th_in_other_years()
    {
        var leap = Birthday.On(2, 29);
        Assert.True(leap.IsOn(new DateOnly(2027, 2, 28)));
        Assert.False(leap.IsOn(new DateOnly(2028, 2, 28)));
        Assert.True(leap.IsOn(new DateOnly(2028, 2, 29)));
        Assert.Equal(leap, Birthday.Parse(leap.ToString()));
        Assert.Throws<DomainException>(() => Birthday.On(4, 31));
        Assert.Throws<DomainException>(() => Birthday.On(13, 1));
        Assert.Null(Birthday.Parse("2-30"));
    }

    [Fact]
    public void The_person_can_choose_a_birthday_and_give_it_back()
    {
        var (office, desk) = ECorpWithDesk();
        var picked = desk.Birthday;

        office.SetDeskBirthday(desk.Id, Birthday.On(12, 24), Now);
        Assert.Equal(Birthday.On(12, 24), desk.Birthday);

        office.SetDeskBirthday(desk.Id, null, Now);
        Assert.Equal(picked, desk.Birthday);
        Assert.Null(desk.ChosenBirthday);
    }
}
