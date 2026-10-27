using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Routines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Routines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OfficeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Schedule = table.Column<string>(type: "TEXT", nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<long>(type: "INTEGER", nullable: true),
                    NextRunAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Routines_Offices_OfficeId",
                        column: x => x.OfficeId,
                        principalTable: "Offices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoutineRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoutineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OfficeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ByHand = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    FinishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Log = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineRuns_Offices_OfficeId",
                        column: x => x.OfficeId,
                        principalTable: "Offices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoutineRuns_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeenItems",
                columns: table => new
                {
                    RoutineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ChangedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeenItems", x => new { x.RoutineId, x.Key });
                    table.ForeignKey(
                        name: "FK_SeenItems_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoutineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OfficeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DecidedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Proposals_RoutineRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "RoutineRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Proposals_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_OfficeId_Status",
                table: "Proposals",
                columns: new[] { "OfficeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_RoutineId",
                table: "Proposals",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_RunId",
                table: "Proposals",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineRuns_OfficeId",
                table: "RoutineRuns",
                column: "OfficeId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineRuns_RoutineId_StartedAt",
                table: "RoutineRuns",
                columns: new[] { "RoutineId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Routines_NextRunAt",
                table: "Routines",
                column: "NextRunAt");

            migrationBuilder.CreateIndex(
                name: "IX_Routines_OfficeId",
                table: "Routines",
                column: "OfficeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Proposals");

            migrationBuilder.DropTable(
                name: "SeenItems");

            migrationBuilder.DropTable(
                name: "RoutineRuns");

            migrationBuilder.DropTable(
                name: "Routines");
        }
    }
}
