using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Shiori;
using Openspace.Infrastructure.Persistence;

namespace Openspace.Infrastructure.Shiori;

/// <summary>
/// One row of the ShioriSessions table. The transcript and the whiteboard are kept as JSON: they
/// are always read and written whole with their session, never searched in SQL (the vault's
/// index searches the session notes).
/// </summary>
internal sealed class ShioriSessionRow
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Person { get; set; } = string.Empty;

    public ShioriMode Mode { get; set; }

    public string RuntimeSessionId { get; set; } = string.Empty;

    public bool RuntimeStarted { get; set; }

    public string Token { get; set; } = string.Empty;

    public string NotePath { get; set; } = string.Empty;

    public string Transcript { get; set; } = "[]";

    public string Board { get; set; } = "[]";

    public int NextItem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }
}

internal sealed class ShioriSessionConfiguration : IEntityTypeConfiguration<ShioriSessionRow>
{
    public void Configure(EntityTypeBuilder<ShioriSessionRow> builder)
    {
        builder.ToTable("ShioriSessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Title).IsRequired();
        builder.Property(s => s.Person).IsRequired();
        builder.Property(s => s.Mode).HasConversion<string>();
        builder.Property(s => s.RuntimeSessionId).IsRequired();
        builder.Property(s => s.Token).IsRequired();
        builder.Property(s => s.NotePath).IsRequired();
        builder.Property(s => s.Transcript).IsRequired();
        builder.Property(s => s.Board).IsRequired();
        builder.HasIndex(s => s.Token).IsUnique();
        builder.HasIndex(s => s.UpdatedAt);
    }
}

/// <summary>
/// Sessions in Openspace's database. A singleton like the sessions service that uses it, so each
/// call takes a database context of its own: answers are saved from the background, long after the
/// request that asked for them.
/// </summary>
internal sealed class ShioriSessionStore(IServiceScopeFactory scopes) : IShioriSessionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<ShioriSession?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var row = await Rows(scope).AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToSession(row);
    }

    public async Task<ShioriSession?> FindByTokenAsync(string token, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var row = await Rows(scope).AsNoTracking().FirstOrDefaultAsync(s => s.Token == token, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToSession(row);
    }

    public async Task<IReadOnlyList<ShioriSession>> ListAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var rows = await Rows(scope).AsNoTracking().OrderByDescending(s => s.UpdatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(ToSession)];
    }

    public async Task AddAsync(ShioriSession session, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenspaceDbContext>();
        db.Set<ShioriSessionRow>().Add(ToRow(session));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(ShioriSession session, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenspaceDbContext>();
        db.Set<ShioriSessionRow>().Update(ToRow(session));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbSet<ShioriSessionRow> Rows(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<OpenspaceDbContext>().Set<ShioriSessionRow>();

    private static ShioriSessionRow ToRow(ShioriSession session) => new()
    {
        Id = session.Id,
        Title = session.Title,
        Person = session.Person,
        Mode = session.Mode,
        RuntimeSessionId = session.RuntimeSessionId,
        RuntimeStarted = session.RuntimeStarted,
        Token = session.Token,
        NotePath = session.NotePath,
        Transcript = JsonSerializer.Serialize(session.Transcript, Json),
        Board = JsonSerializer.Serialize(session.Board, Json),
        NextItem = session.NextItem,
        CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt,
        EndedAt = session.EndedAt,
    };

    private static ShioriSession ToSession(ShioriSessionRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Person = row.Person,
        Mode = row.Mode,
        RuntimeSessionId = row.RuntimeSessionId,
        RuntimeStarted = row.RuntimeStarted,
        Token = row.Token,
        NotePath = row.NotePath,
        Transcript = JsonSerializer.Deserialize<List<TranscriptEntry>>(row.Transcript, Json) ?? [],
        Board = JsonSerializer.Deserialize<List<WhiteboardItem>>(row.Board, Json) ?? [],
        NextItem = row.NextItem,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        EndedAt = row.EndedAt,
    };
}
