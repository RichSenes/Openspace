using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Openspace.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the context without starting the API.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OpenspaceDbContext>
{
    public OpenspaceDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OpenspaceDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
