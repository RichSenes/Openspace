using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeskShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Shift",
                table: "Desks",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Always");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Shift",
                table: "Desks");
        }
    }
}
