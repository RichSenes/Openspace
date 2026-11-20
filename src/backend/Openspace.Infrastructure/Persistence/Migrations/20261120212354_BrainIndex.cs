using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrainIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The vault's notes for search: FTS5 has no EF model, so it is made by hand. Only the
            // title and body are searched; the other columns ride along to rank and show results.
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE IF NOT EXISTS BrainNotes USING fts5(
                    Path UNINDEXED, Title, Body, Kind UNINDEXED, Status UNINDEXED,
                    Offices UNINDEXED, Rooms UNINDEXED, Paths UNINDEXED, Review UNINDEXED, Modified UNINDEXED,
                    tokenize = 'unicode61 remove_diacritics 2');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS BrainNotes;");
        }
    }
}
