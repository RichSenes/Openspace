using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RunBranch",
                table: "WorkTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunSessionId",
                table: "WorkTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunWorkspacePath",
                table: "WorkTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepositoryPath",
                table: "Offices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_RunSessionId",
                table: "WorkTasks",
                column: "RunSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_RunSessionId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "RunBranch",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "RunSessionId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "RunWorkspacePath",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "RepositoryPath",
                table: "Offices");
        }
    }
}
