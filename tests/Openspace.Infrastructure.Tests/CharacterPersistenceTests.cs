using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Offices;
using Openspace.Domain.Offices;

namespace Openspace.Infrastructure.Tests;

/// <summary>What makes a desk an inhabitant, kept in SQLite.</summary>
public sealed class CharacterPersistenceTests : IAsyncLifetime
{
    private readonly SqliteFixture _db = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Traits_and_a_chosen_birthday_survive_a_round_trip_and_old_desks_get_the_traits_of_their_id()
    {
        OfficeId office;
        DeskId chosen, old;
        await using (var scope = _db.Scope())
        {
            var commands = scope.ServiceProvider.GetRequiredService<OfficeCommands>();
            office = await commands.OpenOfficeAsync("E Corp", default);
            var room = await commands.OpenRoomAsync(office, "Web", default);
            chosen = await commands.AddDeskAsync(office, room, "Frontend", "frontend-dev", null, null, default,
                personality: Personality.Of([Trait.Shy, Trait.DryHumoured]), birthday: Birthday.On(2, 29));
            old = await commands.AddDeskAsync(office, room, "Backend", "backend-dev", null, null, default);
        }

        await using (var scope = _db.Scope())
        {
            // A desk from before traits: stored with none.
            var db = scope.ServiceProvider.GetRequiredService<Persistence.OpenspaceDbContext>();
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync("UPDATE Desks SET Traits = '' WHERE Name = 'Backend'"));
        }

        await using (var scope = _db.Scope())
        {
            var view = await scope.ServiceProvider.GetRequiredService<OfficeQueries>().GetAsync(office, default);
            var desks = view.Rooms.SelectMany(r => r.Desks).ToDictionary(d => d.Id);
            Assert.Equal([Trait.Shy, Trait.DryHumoured], desks[chosen.Value].Traits);
            Assert.Equal(new BirthdayView(2, 29, true), desks[chosen.Value].Birthday);
            Assert.Equal(Personality.FromSeed(old.Value).Traits, desks[old.Value].Traits);
            Assert.False(desks[old.Value].Birthday.Chosen);
        }
    }

    [Fact]
    public async Task Trust_and_shift_survive_a_round_trip_and_start_as_today()
    {
        OfficeId office;
        DeskId set, plain;
        await using (var scope = _db.Scope())
        {
            var commands = scope.ServiceProvider.GetRequiredService<OfficeCommands>();
            office = await commands.OpenOfficeAsync("E Corp", default);
            var room = await commands.OpenRoomAsync(office, "Web", default);
            set = await commands.AddDeskAsync(office, room, "Frontend", "frontend-dev", null, null, default);
            plain = await commands.AddDeskAsync(office, room, "Backend", "backend-dev", null, null, default);
        }

        await using (var scope = _db.Scope())
        {
            var commands = scope.ServiceProvider.GetRequiredService<OfficeCommands>();
            await commands.SetDeskTrustAsync(office, set, TrustedAction.SafeRequests, TrustLevel.AsksFirst, default);
            await commands.SetDeskShiftAsync(office, set, Shift.Night, default);
        }

        await using (var scope = _db.Scope())
        {
            var view = await scope.ServiceProvider.GetRequiredService<OfficeQueries>().GetAsync(office, default);
            var desks = view.Rooms.SelectMany(r => r.Desks).ToDictionary(d => d.Id);
            Assert.Equal((TrustLevel.AsksFirst, Shift.Night), (desks[set.Value].Trust[TrustedAction.SafeRequests], desks[set.Value].Shift));
            Assert.Equal((TrustLevel.Alone, Shift.Always), (desks[plain.Value].Trust[TrustedAction.SafeRequests], desks[plain.Value].Shift));
        }
    }

    [Fact]
    public async Task An_agreement_survives_a_round_trip_with_its_answer()
    {
        OfficeId office;
        Domain.Work.AgreementId id;
        var (firstTask, secondTask) = (Domain.Work.WorkTaskId.New(), Domain.Work.WorkTaskId.New());
        var now = new DateTimeOffset(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
        await using (var scope = _db.Scope())
        {
            var commands = scope.ServiceProvider.GetRequiredService<OfficeCommands>();
            office = await commands.OpenOfficeAsync("E Corp", default);
            var room = await commands.OpenRoomAsync(office, "Web", default);
            var a = await commands.AddDeskAsync(office, room, "Frontend", "frontend-dev", null, null, default);
            var b = await commands.AddDeskAsync(office, room, "Screens", "designer", null, null, default);
            var agreement = Domain.Work.Agreement.Open(office, a, firstTask, b, secondTask, "src/web/cart.ts", now);
            agreement.Record("Frontend first.", "Screens first.", 4, null, "Even.", "Agreements/x.md", now);
            agreement.Decide(secondTask, "elliot", now.AddHours(1));
            id = agreement.Id;
            scope.ServiceProvider.GetRequiredService<Application.Abstractions.IAgreementRepository>().Add(agreement);
            await scope.ServiceProvider.GetRequiredService<Application.Abstractions.IUnitOfWork>().SaveChangesAsync(default);
        }

        await using (var scope = _db.Scope())
        {
            var found = Assert.Single(await scope.ServiceProvider.GetRequiredService<Application.Abstractions.IAgreementRepository>().ListAsync(office, default));
            Assert.Equal(id, found.Id);
            Assert.Equal((Domain.Work.AgreementStatus.DecidedByPerson, secondTask, firstTask, "elliot", 4), (found.Status, found.GoesFirst, found.GoesSecond, found.DecidedBy, found.Turns));
            Assert.Equal(("Frontend first.", "Agreements/x.md", now.AddHours(1)), (found.FirstProposal, found.MinutesPath, found.ClosedAt));
        }
    }
}
