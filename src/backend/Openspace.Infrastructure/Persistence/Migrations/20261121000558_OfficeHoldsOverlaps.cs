using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfficeHoldsOverlaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Offices that exist already hold overlapping work too: it is the constitution's rule 6.
            migrationBuilder.AddColumn<bool>(
                name: "HoldOverlappingWork",
                table: "Offices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoldOverlappingWork",
                table: "Offices");
        }
    }
}
