using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "WorkTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "StatusChangedAt",
                table: "WorkTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Tasks from before this change: the agent's latest note, written as its work went to
            // review, stands in for the last move; their creation when there is none.
            migrationBuilder.Sql("""
                UPDATE WorkTasks SET StatusChangedAt = COALESCE(
                    (SELECT MAX(n.WrittenAt) FROM TaskNotes n WHERE n.WorkTaskId = WorkTasks.Id AND n.AuthorKind = 'Desk'),
                    CreatedAt);
                """);

            migrationBuilder.CreateTable(
                name: "TaskManualChecks",
                columns: table => new
                {
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    What = table.Column<string>(type: "TEXT", nullable: false),
                    How = table.Column<string>(type: "TEXT", nullable: false),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskManualChecks", x => new { x.WorkTaskId, x.Number });
                    table.ForeignKey(
                        name: "FK_TaskManualChecks_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskRoundUsage",
                columns: table => new
                {
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Input = table.Column<long>(type: "INTEGER", nullable: false),
                    Output = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheRead = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheWrite = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRoundUsage", x => new { x.WorkTaskId, x.Round });
                    table.ForeignKey(
                        name: "FK_TaskRoundUsage_WorkTasks_WorkTaskId",
                        column: x => x.WorkTaskId,
                        principalTable: "WorkTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskManualChecks");

            migrationBuilder.DropTable(
                name: "TaskRoundUsage");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "WorkTasks");
        }
    }
}
