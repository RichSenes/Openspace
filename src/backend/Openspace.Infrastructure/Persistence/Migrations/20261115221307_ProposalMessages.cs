using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MessageId",
                table: "Proposals",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "Proposals");
        }
    }
}
